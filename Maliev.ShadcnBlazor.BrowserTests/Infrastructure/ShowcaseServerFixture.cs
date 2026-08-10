using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

public sealed class ShowcaseServerFixture : IAsyncLifetime
{
    private Process? _process;
    public Uri BaseUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        BaseUri = new Uri($"http://127.0.0.1:{port}");

        var root = FindRoot();
        var project = Path.Combine(root, "Maliev.ShadcnBlazor.Showcase", "Maliev.ShadcnBlazor.Showcase.csproj");
        _process = Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            Arguments = $"run --project \"{project}\" -c Release --no-build --urls {BaseUri}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the showcase host.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(await _process.StandardError.ReadToEndAsync());
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

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        _process?.Dispose();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
