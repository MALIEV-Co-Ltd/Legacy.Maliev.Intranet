using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

/// <summary>Observes exact request termination, including canceled WASM fetch bodies, without accepting a different response.</summary>
internal sealed class CustomerDocumentRequestCompletion : IDisposable
{
    private readonly IPage page;
    private readonly object sync = new();
    private readonly Dictionary<IRequest, string> ended = new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource<string> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IRequest? target;

    public CustomerDocumentRequestCompletion(IPage page)
    {
        this.page = page;
        page.RequestFinished += OnFinished;
        page.RequestFailed += OnFailed;
    }

    public async Task WaitAsync(IResponse response)
    {
        lock (sync)
        {
            if (target is not null) throw new InvalidOperationException("A completion observer belongs to one exact response.");
            target = response.Request;
            if (ended.TryGetValue(target, out var kind)) completed.TrySetResult(kind);
        }
        var termination = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var root = Environment.GetEnvironmentVariable("TASK4_BROWSER_EVIDENCE");
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, $"customer-document-request-terminal-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new { Termination = termination, response.Request.Method, Path = new Uri(response.Url).AbsolutePath, response.Status }));
        }
        catch { /* Diagnostic writes must preserve the original test outcome. */ }
    }

    private void OnFinished(object? sender, IRequest request) => OnEnded(request, "Finished");
    private void OnFailed(object? sender, IRequest request) => OnEnded(request, "Failed");

    private void OnEnded(IRequest request, string kind)
    {
        lock (sync)
        {
            ended[request] = kind;
            if (ReferenceEquals(target, request)) completed.TrySetResult(kind);
        }
    }

    public void Dispose()
    {
        page.RequestFinished -= OnFinished;
        page.RequestFailed -= OnFailed;
    }
}
