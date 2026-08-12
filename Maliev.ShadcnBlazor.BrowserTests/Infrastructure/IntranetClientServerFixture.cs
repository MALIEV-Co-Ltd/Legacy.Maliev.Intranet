using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

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

    public Task DisposeAsync() => StopHostAsync(_process);

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
