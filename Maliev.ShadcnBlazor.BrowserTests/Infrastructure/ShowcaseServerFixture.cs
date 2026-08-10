using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

public sealed class ShowcaseServerFixture : IAsyncLifetime
{
    private const int MaximumStartupAttempts = 3;
    private const int MaximumDiagnosticCharacters = 16 * 1024;
    private Process? _process;
    private BoundedDiagnostics? _standardOutput;
    private BoundedDiagnostics? _standardError;
    private Task? _standardOutputDrain;
    private Task? _standardErrorDrain;
    public Uri BaseUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var root = FindRoot();
        var project = Path.Combine(root, "Maliev.ShadcnBlazor.Showcase", "Maliev.ShadcnBlazor.Showcase.csproj");
        for (var attempt = 1; attempt <= MaximumStartupAttempts; attempt++)
        {
            BaseUri = SelectBaseUri();
            StartHost(root, project);
            try
            {
                await WaitForReadinessAsync();
                return;
            }
            catch (Exception exception)
            {
                var diagnostics = await StopHostAsync();
                if (attempt < MaximumStartupAttempts && IsAddressInUse($"{exception}\n{diagnostics}"))
                    continue;
                throw;
            }
        }

        throw new InvalidOperationException("Showcase startup attempts were exhausted.");
    }

    public Task DisposeAsync() => StopHostAsync();

    internal static bool IsAddressInUse(string diagnostics) =>
        diagnostics.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
        diagnostics.Contains("only one usage of each socket address", StringComparison.OrdinalIgnoreCase);

    private static Uri SelectBaseUri()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}");
    }

    private void StartHost(string root, string project)
    {
        _process = Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            Arguments = $"run --project \"{project}\" -c Release --no-build --urls {BaseUri}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the showcase host.");

        _standardOutput = new BoundedDiagnostics(MaximumDiagnosticCharacters);
        _standardError = new BoundedDiagnostics(MaximumDiagnosticCharacters);
        _standardOutputDrain = DrainAsync(_process.StandardOutput, _standardOutput);
        _standardErrorDrain = DrainAsync(_process.StandardError, _standardError);
    }

    private async Task WaitForReadinessAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
                throw new InvalidOperationException($"Showcase host exited. {await ReadDiagnosticsAsync()}");
            try
            {
                using var response = await http.GetAsync(BaseUri);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Showcase did not become ready at {BaseUri}.");
    }

    private async Task<string> StopHostAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        await ReadDiagnosticsAsync();
        var diagnostics = FormatDiagnostics();
        _process?.Dispose();
        _process = null;
        _standardOutput = null;
        _standardError = null;
        _standardOutputDrain = null;
        _standardErrorDrain = null;
        return diagnostics;
    }

    private async Task<string> ReadDiagnosticsAsync()
    {
        if (_standardOutputDrain is not null && _standardErrorDrain is not null)
            await Task.WhenAll(_standardOutputDrain, _standardErrorDrain);
        return FormatDiagnostics();
    }

    private string FormatDiagnostics() => $"stdout: {_standardOutput}\nstderr: {_standardError}";

    private static async Task DrainAsync(StreamReader reader, BoundedDiagnostics diagnostics)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
            diagnostics.Append(buffer.AsSpan(0, read));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
