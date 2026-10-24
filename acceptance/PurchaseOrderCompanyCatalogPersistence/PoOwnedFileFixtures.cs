using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Google;
using Google.Cloud.Storage.V1;
using Legacy.Maliev.FileService.Application.Interfaces;
using Legacy.Maliev.FileService.Application.Models;
using Legacy.Maliev.FileService.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace SupplierCatalogPersistence.Acceptance;

// Synthetic external protocols only: the production storage/scanner adapters remain selected.
// Source: File d478d2674b57a25c939f62aa838efbedd8591055, ControlledCloud,
// LoopbackClamAvServer, and HostedFinancialCompletionProfileTests.VerifySignature.
internal sealed class PoOwnedFileFixtures
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const string Bucket = "maliev.com";
    private readonly object gate = new();
    private readonly Dictionary<string, Stored> objects = new(StringComparer.Ordinal);
    private readonly List<byte[]> scanned = [];
    private readonly List<Task> connections = [];
    private readonly SupplierResourceScope owner;
    private readonly CancellationTokenSource lifetime;
    private readonly HostedAcceptanceSigningIdentity identity;
    private readonly TcpListener reads;
    private readonly TcpListener scans;
    private Task readLoop = Task.CompletedTask;
    private Task scanLoop = Task.CompletedTask;
    private long generation = 16;
    private int infected;
    private int attempts;
    private int uploads;
    private int copies;
    private int deletes;
    private readonly DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(15);
    internal StorageClient Client { get; private set; } = null!;
    internal UrlSigner Signer => identity.Signer;
    internal HostedAcceptanceSignedReadOrigin SignedOrigin { get; private set; } = null!;
    internal int ScannerPort => ((IPEndPoint)scans.LocalEndpoint).Port;
    internal Uri ReadOrigin { get; private set; } = null!;
    internal int ScannedInfected { get { lock (gate) return infected; } }
    internal int ScanAttempts { get { lock (gate) return attempts; } }
    internal IReadOnlyList<byte[]> ScannedBytes { get { lock (gate) return scanned.Select(bytes => bytes.ToArray()).ToArray(); } }

    private PoOwnedFileFixtures(SupplierResourceScope owner)
    {
        this.owner = owner;
        lifetime = owner.Acquire("po-file-protocol-cancellation", () => CancellationTokenSource.CreateLinkedTokenSource(owner.Token),
            (value, _) => { value.Dispose(); return Task.CompletedTask; }, 3);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        identity = owner.Acquire("po-file-sdk-signing-identity", () => new HostedAcceptanceSigningIdentity(),
            (value, _) => { value.Dispose(); return Task.CompletedTask; }, 3);
        reads = owner.Acquire("po-file-signed-read-listener", () => new TcpListener(IPAddress.Loopback, 0),
            (value, _) => { value.Stop(); return Task.CompletedTask; }, 1);
        scans = owner.Acquire("po-file-instream-listener", () => new TcpListener(IPAddress.Loopback, 0),
            (value, _) => { value.Stop(); return Task.CompletedTask; }, 1);
    }

    internal static async Task<PoOwnedFileFixtures> StartAsync(SupplierResourceScope owner)
    {
        var fixture = new PoOwnedFileFixtures(owner);
        var settlement = owner.Register("po-file-protocol-task-settlement", fixture.StopAsync, 2);
        await owner.StartAsync(settlement, _ =>
        {
            fixture.reads.Start(16);
            fixture.scans.Start(16);
            fixture.ReadOrigin = new Uri($"http://127.0.0.1:{((IPEndPoint)fixture.reads.LocalEndpoint).Port}/");
            fixture.SignedOrigin = new HostedAcceptanceSignedReadOrigin(fixture.ReadOrigin, fixture.expires, TimeProvider.System);
            fixture.ConfigureSdk();
            fixture.readLoop = fixture.AcceptAsync(fixture.reads, fixture.ReadAsync);
            fixture.scanLoop = fixture.AcceptAsync(fixture.scans, fixture.ScanAsync);
            return Task.CompletedTask;
        });
        return fixture;
    }

    private void ConfigureSdk()
    {
        var mock = owner.Acquire("po-file-strict-storage-sdk", () => new Mock<StorageClient>(MockBehavior.Strict),
            (_, _) => { lock (gate) objects.Clear(); return Task.CompletedTask; }, 3);
        mock.Setup(value => value.UploadObjectAsync(It.IsAny<StorageObject>(), It.IsAny<Stream>(), It.IsAny<UploadObjectOptions>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(call => UploadAsync((StorageObject)call.Arguments[0], (Stream)call.Arguments[1],
                (UploadObjectOptions)call.Arguments[2], (CancellationToken)call.Arguments[3])));
        mock.Setup(value => value.GetObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<GetObjectOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, GetObjectOptions, CancellationToken>((bucket, name, options, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    var item = Get(bucket, name);
                    if (options?.Generation is long requested && requested != item.Generation) throw Error(HttpStatusCode.NotFound);
                    return Task.FromResult(Metadata(name, item));
                }
            });
        mock.Setup(value => value.CopyObjectAsync(Bucket, It.IsAny<string>(), Bucket, It.IsAny<string>(), It.IsAny<CopyObjectOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, string, CopyObjectOptions, CancellationToken>((sourceBucket, source, destinationBucket, destination, options, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    var original = Get(sourceBucket, source);
                    if (destinationBucket != Bucket || !source.StartsWith("_quarantine/", StringComparison.Ordinal) ||
                        destination.StartsWith("_quarantine/", StringComparison.Ordinal) || options.IfGenerationMatch != 0 || options.SourceGeneration != original.Generation ||
                        options.IfSourceGenerationMatch != original.Generation || objects.ContainsKey(destination)) throw Error(HttpStatusCode.PreconditionFailed);
                    var copy = new Stored(++generation, original.Bytes.ToArray(), original.ContentType);
                    objects.Add(destination, copy);
                    copies++;
                    return Task.FromResult(Metadata(destination, copy));
                }
            });
        mock.Setup(value => value.DeleteObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<DeleteObjectOptions>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, DeleteObjectOptions, CancellationToken>((bucket, name, options, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    var item = Get(bucket, name);
                    if (options?.IfGenerationMatch != item.Generation || (options.Generation is long requested && requested != item.Generation))
                        throw Error(HttpStatusCode.PreconditionFailed);
                    objects.Remove(name);
                    deletes++;
                    return Task.CompletedTask;
                }
            });
        Client = mock.Object;
    }

    private async Task<StorageObject> UploadAsync(StorageObject item, Stream content, UploadObjectOptions options, CancellationToken token)
    {
        if (item.Bucket != Bucket || string.IsNullOrWhiteSpace(item.Name) || !item.Name.StartsWith("_quarantine/", StringComparison.Ordinal) ||
            options.IfGenerationMatch != 0) throw Error(HttpStatusCode.PreconditionFailed);
        using var bytes = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var count = await content.ReadAsync(buffer, token);
            if (count == 0) break;
            if (bytes.Length + count > MaximumBytes) throw new InvalidDataException("Synthetic file boundary size exceeded.");
            bytes.Write(buffer, 0, count);
        }
        lock (gate)
        {
            if (objects.ContainsKey(item.Name)) throw Error(HttpStatusCode.PreconditionFailed);
            var stored = new Stored(++generation, bytes.ToArray(), item.ContentType);
            objects.Add(item.Name, stored);
            uploads++;
            return Metadata(item.Name, stored);
        }
    }

    internal byte[] ReadObject(string bucket, string objectName) { lock (gate) return Get(bucket, objectName).Bytes.ToArray(); }
    private Stored Get(string bucket, string name) => bucket == Bucket && objects.TryGetValue(name, out var item) ? item : throw Error(HttpStatusCode.NotFound);
    private static StorageObject Metadata(string name, Stored item) => new() { Bucket = Bucket, Name = name, Generation = item.Generation, Size = (ulong)item.Bytes.Length, ContentType = item.ContentType };
    private static GoogleApiException Error(HttpStatusCode code) => new("storage", "Synthetic owned provider rejection") { HttpStatusCode = code };

    private async Task AcceptAsync(TcpListener listener, Func<TcpClient, CancellationToken, Task> handler)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(lifetime.Token);
                lock (gate)
                {
                    if (connections.Count >= 128)
                    {
                        socket.Dispose();
                        throw new InvalidDataException("Synthetic protocol connection bound exceeded.");
                    }
                }
                // The scope-owned accept loop retains every socket's finally and settlement task.
                // Even a synchronous handler fault settles within ServeAsync before the next accept.
                var task = ServeAsync(socket, handler);
                lock (gate) connections.Add(task);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient socket, Func<TcpClient, CancellationToken, Task> handler)
    {
        using (socket)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try { await handler(socket, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }

    private async Task ScanAsync(TcpClient socket, CancellationToken token)
    {
        lock (gate) attempts++;
        await using var stream = socket.GetStream();
        var command = new byte[10];
        await stream.ReadExactlyAsync(command, token);
        if (!command.AsSpan().SequenceEqual("zINSTREAM\0"u8)) throw new InvalidDataException("Unknown scanner command.");
        using var body = new MemoryStream();
        var length = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(length, token);
            var count = BinaryPrimitives.ReadInt32BigEndian(length);
            if (count == 0) break;
            if (count is < 1 or > 65536 || body.Length + count > MaximumBytes) throw new InvalidDataException("Invalid INSTREAM body.");
            var chunk = new byte[count];
            await stream.ReadExactlyAsync(chunk, token);
            body.Write(chunk);
        }
        var complete = body.ToArray();
        var unsafeBytes = complete.AsSpan().IndexOf("EICAR-STANDARD-ANTIVIRUS-TEST-FILE"u8) >= 0;
        lock (gate) { scanned.Add(complete); if (unsafeBytes) infected++; }
        var knownPdf = complete.AsSpan().StartsWith("%PDF-"u8) && complete.AsSpan().IndexOf("%%EOF"u8) >= 0;
        await stream.WriteAsync(unsafeBytes ? "stream: Synthetic.Eicar FOUND\0"u8.ToArray() :
            knownPdf ? "stream: OK\0"u8.ToArray() : "stream: Unknown synthetic input ERROR\0"u8.ToArray(), token);
    }

    private async Task ReadAsync(TcpClient socket, CancellationToken token)
    {
        await using var stream = socket.GetStream();
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 16384)
        {
            await stream.ReadExactlyAsync(one, token);
            header.Add(one[0]);
            if (header.Count >= 4 && header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray())) break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.None);
        var request = lines[0].Split(' ');
        byte[]? body = null;
        try
        {
            if (header.Count >= 16384 || request.Length != 3 || request[0] != "GET" || request[2] != "HTTP/1.1" || !request[1].StartsWith('/')) throw new InvalidDataException();
            var hosts = lines.Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (hosts.Length != 1 || hosts[0][5..].Trim() != ReadOrigin.Authority || lines.Any(line => line.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) ||
                (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && line[15..].Trim() != "0"))) throw new InvalidDataException();
            var uri = new Uri(ReadOrigin, request[1]);
            if (!VerifyRead(uri)) throw new InvalidDataException();
            var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (Uri.UnescapeDataString(segments[1]).StartsWith("_quarantine/", StringComparison.Ordinal)) throw new InvalidDataException();
            var query = Query(uri);
            lock (gate)
            {
                var stored = Get(Uri.UnescapeDataString(segments[0]), Uri.UnescapeDataString(segments[1]));
                if (stored.Generation != long.Parse(query["generation"], CultureInfo.InvariantCulture)) throw new InvalidDataException();
                body = stored.Bytes.ToArray();
            }
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or KeyNotFoundException or ArgumentException or GoogleApiException or CryptographicException or IndexOutOfRangeException) { }
        var response = body is null ? "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n" :
            $"HTTP/1.1 200 OK\r\nContent-Type: application/pdf\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), token);
        if (body is not null) await stream.WriteAsync(body, token);
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private bool VerifyRead(Uri uri)
    {
        if (DateTimeOffset.UtcNow >= expires || uri.Scheme != ReadOrigin.Scheme || uri.Authority != ReadOrigin.Authority || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var query = Query(uri);
        if (query.Count != 8 || query["X-Goog-Algorithm"] != "GOOG4-RSA-SHA256" || query["X-Goog-SignedHeaders"] != "host" ||
            !query.ContainsKey("response-content-disposition") || !long.TryParse(query["generation"], out var version) || version <= 0) return false;
        var date = DateTimeOffset.ParseExact(query["X-Goog-Date"], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        if (!int.TryParse(query["X-Goog-Expires"], out var duration) || duration is < 1 or > 604800 || date > DateTimeOffset.UtcNow.AddSeconds(5) || date.AddSeconds(duration) <= DateTimeOffset.UtcNow) return false;
        var scope = query["X-Goog-Credential"].Split('/', 2);
        if (scope.Length != 2 || scope[0] != "hosted-file-signing@example.invalid" || scope[1] != $"{date:yyyyMMdd}/auto/storage/goog4_request") return false;
        var pairs = uri.Query.TrimStart('?').Split('&');
        var canonicalQuery = string.Join('&', pairs.Where(pair => !pair.StartsWith("X-Goog-Signature=", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        // SDK signs the host without its Options.Port; exact port is independently admitted above.
        var canonical = $"GET\n{uri.AbsolutePath}\n{canonicalQuery}\nhost:{uri.Host}\n\nhost\nUNSIGNED-PAYLOAD";
        var payload = $"GOOG4-RSA-SHA256\n{query["X-Goog-Date"]}\n{scope[1]}\n{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
        using var key = RSA.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(identity.VerificationPublicKey), out _);
        return key.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromHexString(query["X-Goog-Signature"]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private async Task StopAsync(CancellationToken token)
    {
        await lifetime.CancelAsync();
        reads.Stop();
        scans.Stop();
        await Task.WhenAll(readLoop, scanLoop).WaitAsync(token);
        Task[] tasks;
        lock (gate) tasks = connections.ToArray();
        await Task.WhenAll(tasks).WaitAsync(token);
    }

    internal object SourceReceipt()
    {
        lock (gate) return new
        {
            syntheticExternalProtocols = true,
            liveCloud = false,
            malwareEngine = false,
            fileSource = "d478d2674b57a25c939f62aa838efbedd8591055",
            storagePatternBlob = "6a8a0ab439b9a3a802dee370968b25983a58d4f8",
            scannerPatternBlob = "e5c4fd3e9dc5ec9417edf91d1da7c172032a7ec6",
            signaturePatternBlob = "eedcf5a3a4a1cc964b4077ea99fa43803ab21235",
            uploads, copies, deletes, scanAttempts = attempts, scannedInfected = infected,
            scannedLengths = scanned.Select(bytes => bytes.Length).ToArray(),
            scannedSha256 = scanned.Select(bytes => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()).ToArray()
        };
    }

    private sealed record Stored(long Generation, byte[] Bytes, string ContentType);
}

public sealed class PoOwnedFileFixtureControls
{
    [Fact]
    public async Task SyntheticFileProtocols_CompleteScanConditionalPromotionSignedReadAndDenialReleaseOwner()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(2));
        try
        {
            var fixture = await PoOwnedFileFixtures.StartAsync(owner);
            var storage = new GoogleCloudObjectStorage(fixture.Client, fixture.Signer, null, fixture.SignedOrigin);
            var scanner = new ClamAvFileSafetyScanner(Options.Create(new MalwareScannerOptions { Host = "127.0.0.1", Port = fixture.ScannerPort, TimeoutSeconds = 5 }), NullLogger<ClamAvFileSafetyScanner>.Instance);
            var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 70000) + "\n%%EOF");
            using var input = new MemoryStream(pdf);
            Assert.Equal(17, await storage.UploadGenerationAsync("maliev.com", "_quarantine/control.pdf", "application/pdf", input, owner.Token));
            using var scanInput = new MemoryStream(pdf);
            Assert.Equal(InstantQuoteScanResult.Clean, await ((IInstantQuoteFileSafetyScanner)scanner).ScanAsync(scanInput, owner.Token));
            Assert.Equal(pdf, Assert.Single(fixture.ScannedBytes));
            Assert.True(await storage.MoveAsync("maliev.com", "_quarantine/control.pdf", "maliev.com", "clean/control.pdf", owner.Token));
            Assert.Equal(pdf, fixture.ReadObject("maliev.com", "clean/control.pdf"));
            var evidence = await storage.GetEvidenceAsync("maliev.com", "clean/control.pdf", owner.Token);
            var url = await storage.CreateSignedGenerationReadUriAsync("maliev.com", "clean/control.pdf", evidence!.Generation, TimeSpan.FromMinutes(1), owner.Token);
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            Assert.Equal(pdf, await client.GetByteArrayAsync(url, owner.Token));
            using var denied = await client.GetAsync(new Uri(url.AbsoluteUri.Replace("generation=18", "generation=19", StringComparison.Ordinal)), owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var unsigned = await client.GetAsync(new Uri(fixture.ReadOrigin, "maliev.com/clean/control.pdf"), owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
            using var eicar = new MemoryStream("EICAR-STANDARD-ANTIVIRUS-TEST-FILE"u8.ToArray());
            Assert.Equal(InstantQuoteScanResult.Unsafe, await ((IInstantQuoteFileSafetyScanner)scanner).ScanAsync(eicar, owner.Token));
            Assert.Equal(1, fixture.ScannedInfected);
            Assert.Equal(2, fixture.ScanAttempts);
            using var unknown = new MemoryStream("unknown protocol input"u8.ToArray());
            Assert.Equal(InstantQuoteScanResult.Unavailable, await ((IInstantQuoteFileSafetyScanner)scanner).ScanAsync(unknown, owner.Token));
        }
        finally { await owner.DisposeAsync(); }
        Assert.True(owner.Released);
        Assert.DoesNotContain(owner, SupplierResourceScope.Unresolved);
    }
}
