using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.Intranet.BrowserTests.Infrastructure;

public sealed class IntranetClientServerFixture : IAsyncLifetime
{
    private const int MaximumDiagnosticCharacters = 16 * 1024;
    private Process? _process;
    private BoundedDiagnostics? _standardOutput;
    private BoundedDiagnostics? _standardError;
    private Task? _standardOutputDrain;
    private Task? _standardErrorDrain;

#if DEBUG
    internal const string BuildConfiguration = "Debug";
#else
    internal const string BuildConfiguration = "Release";
#endif

    public Uri BaseUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var root = FindRoot();
        var project = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj");
        BaseUri = SelectBaseUri();
        try
        {
            await StartAndWaitForReadinessAsync(
                () => StartHost(root, project),
                _ => WaitForReadinessAsync(),
                StopHostAsync);
        }
        catch
        {
            // StartHost can fail after Process.Start succeeds (for example while attaching
            // redirected diagnostics). Always tear down that exact process before rethrowing.
            if (_process is not null)
                await StopHostAsync(_process);
            throw;
        }
    }

    public Task DisposeAsync() => Task.WhenAll(StopHostAsync(_process), StopPublishedClientAsync());

    private readonly object _publishedGate = new();
    private Task<(Uri BaseUri, string Root)>? _publishedClient;
    private Microsoft.AspNetCore.Builder.WebApplication? _publishedHost;
    private DirectoryInfo? _publishedDirectory;
    private bool _publishedClosing;
    private Task? _publishedStop;
    private Task? _publishedDispose;
    private (int Pid, DateTime StartedUtc)? _publishIdentity;
    private bool _publishExited;

    /// <summary>Publishes the actual Client once, only when the production-form cases request it.</summary>
    public Task<(Uri BaseUri, string Root)> GetPublishedClientAsync()
    {
        lock (_publishedGate)
        {
            if (_publishedClosing) throw new ObjectDisposedException(nameof(IntranetClientServerFixture));
            return _publishedClient ??= PublishAndStartClientAsync();
        }
    }

    private async Task<(Uri BaseUri, string Root)> PublishAndStartClientAsync()
    {
        // The existing validation job is Ubuntu. GNU timeout owns a separate process
        // group and terminates the complete publish invocation at 8m (+20s kill grace).
        // No shared compiler/node server is used; no build is started by ordinary cases.
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Published form validation requires the existing Ubuntu validation runner.");
        var root = FindRoot();
        var project = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj");
        _publishedDirectory = Directory.CreateTempSubdirectory("intranet-published-forms-");
        var startInfo = new ProcessStartInfo("timeout")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "--signal=TERM", "--kill-after=20s", "8m", "dotnet", "publish", project,
            "--configuration", BuildConfiguration, "--no-restore", "--nologo",
            "--output", _publishedDirectory.FullName, "--warnaserror", "-m:1", "-nr:false",
            "-p:UseLocalMalievDependencies=true", "-p:UseSharedCompilation=false",
        })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_PROCESSOR_COUNT"] = "1";
        startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        Process process;
        try { process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the bounded Client publish."); }
        catch { _publishExited = true; throw; }
        using (process)
        {
            var failures = new List<Exception>();
            Task? outputDrain = null;
            Task? errorDrain = null;
            try
            {
                _publishIdentity = (process.Id, process.StartTime.ToUniversalTime());
                var output = new BoundedDiagnostics(MaximumDiagnosticCharacters);
                var errors = new BoundedDiagnostics(MaximumDiagnosticCharacters);
                outputDrain = DrainAsync(process.StandardOutput, output);
                errorDrain = DrainAsync(process.StandardError, errors);
                await Task.WhenAll(process.WaitForExitAsync(), outputDrain, errorDrain);
                if (process.ExitCode != 0)
                    failures.Add(new InvalidOperationException("The bounded production Client publish failed."));
            }
            catch (Exception exception) { failures.Add(exception); }
            finally
            {
                // Await the actual timeout-owned invocation and both actual drains,
                // even if setup failed. Never delete output while its writer is live.
                foreach (var operation in new[] { process.WaitForExitAsync(), outputDrain ?? Task.CompletedTask, errorDrain ?? Task.CompletedTask })
                {
                    try { await operation; }
                    catch (Exception exception)
                    {
                        if (!failures.Any(existing => ReferenceEquals(existing, exception))) failures.Add(exception);
                    }
                }
                _publishExited = process.HasExited && _publishIdentity is { } identity && identity.Pid == process.Id;
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Published Client invocation failed.", failures);
        }
        var webRoot = Path.Combine(_publishedDirectory.FullName, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html"))
            || !File.Exists(Path.Combine(webRoot, "_framework", "blazor.webassembly.js")))
            throw new InvalidOperationException("The actual Client publish did not produce the required shell and bootstrap.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production,
            ContentRootPath = _publishedDirectory.FullName,
            WebRootPath = webRoot,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _publishedHost = builder.Build();
        var types = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        types.Mappings[".wasm"] = "application/wasm";
        _publishedHost.UseDefaultFiles();
        _publishedHost.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions { ContentTypeProvider = types });
        _publishedHost.MapFallbackToFile("index.html");
        using var startDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _publishedHost.StartAsync(startDeadline.Token);
        return (new Uri(_publishedHost.Urls.Single()), webRoot);
    }

    private async Task StopPublishedClientAsync()
    {
        Task<(Uri BaseUri, string Root)>? pending;
        lock (_publishedGate)
        {
            _publishedClosing = true;
            pending = _publishedClient;
        }
        if (pending is null) return;
        var failures = new List<Exception>();
        try { await pending; }
        catch (Exception exception) { failures.Add(exception); }
        if (_publishedHost is { } host)
        {
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                _publishedStop = host.StopAsync(stopDeadline.Token);
                await _publishedStop.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) { failures.Add(exception); }
            try
            {
                _publishedDispose = host.DisposeAsync().AsTask();
                await _publishedDispose.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) { failures.Add(exception); }
            if (_publishedDispose?.IsCompletedSuccessfully == true) _publishedHost = null;
        }
        if (_publishExited && _publishedHost is null && _publishedStop?.IsCompleted != false && _publishedDirectory is { } directory)
        {
            // CreateTempSubdirectory allocated this exact directory for this fixture;
            // only remove it after publish terminal and successful host disposal.
            // Pending stop/dispose tasks remain retained above; uncertain output is preserved.
            try { directory.Delete(recursive: true); _publishedDirectory = null; }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count != 0) throw new AggregateException("Published Client fixture did not complete cleanly.", failures);
    }

    internal static async Task<TProcess> StartAndWaitForReadinessAsync<TProcess>(
        Func<TProcess> start,
        Func<TProcess, Task> waitForReadiness,
        Func<TProcess, Task> stop)
    {
        var process = start();
        try
        {
            await waitForReadiness(process);
            return process;
        }
        catch
        {
            await stop(process);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string root, string project, Uri baseUri, string configuration)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseUri.AbsoluteUri);
        return startInfo;
    }

    private Process StartHost(string root, string project)
    {
        _process = Process.Start(CreateStartInfo(root, project, BaseUri, BuildConfiguration))
            ?? throw new InvalidOperationException("Could not start the production Intranet client.");

        _standardOutput = new BoundedDiagnostics(MaximumDiagnosticCharacters);
        _standardError = new BoundedDiagnostics(MaximumDiagnosticCharacters);
        _standardOutputDrain = DrainAsync(_process.StandardOutput, _standardOutput);
        _standardErrorDrain = DrainAsync(_process.StandardError, _standardError);
        return _process;
    }

    private async Task WaitForReadinessAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
                throw new InvalidOperationException($"Production Intranet client exited. {await ReadDiagnosticsAsync()}");
            try
            {
                using var response = await http.GetAsync(BaseUri);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // The dev server is still starting.
            }
            catch (TaskCanceledException)
            {
                // Retry until the bounded readiness deadline.
            }
            await Task.Delay(250);
        }

        throw new TimeoutException($"Production Intranet client did not become ready at {BaseUri}. {FormatDiagnostics()}");
    }

    private async Task StopHostAsync(Process? process)
    {
        try
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the HasExited check and Kill.
                }
                catch (Win32Exception) when (HasExited(process))
                {
                    // Windows can report an already-exited process as a Kill failure.
                }

                try
                {
                    await process.WaitForExitAsync();
                }
                catch (InvalidOperationException)
                {
                    // The process already exited.
                }
            }

            await ReadDiagnosticsAsync();
        }
        finally
        {
            process?.Dispose();
            _process = null;
            _standardOutput = null;
            _standardError = null;
            _standardOutputDrain = null;
            _standardErrorDrain = null;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private async Task<string> ReadDiagnosticsAsync()
    {
        if (_standardOutputDrain is not null && _standardErrorDrain is not null)
            await Task.WhenAll(_standardOutputDrain, _standardErrorDrain);
        return FormatDiagnostics();
    }

    private string FormatDiagnostics() => $"stdout: {_standardOutput}{Environment.NewLine}stderr: {_standardError}";

    private static async Task DrainAsync(StreamReader reader, BoundedDiagnostics diagnostics)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
            diagnostics.Append(buffer.AsSpan(0, read));
    }

    private static Uri SelectBaseUri()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new Uri($"http://127.0.0.1:{port}");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Legacy.Maliev.Intranet root.");
    }
}
