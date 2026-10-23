using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

/// <summary>Read-only Chromium geometry samples during an ordinary locator click.</summary>
internal static class ClickProtocolDiagnostics
{
    // A separate CDP session does not expose Playwright's retry ordinal or its
    // pre-input action point. These samples must never be called actual attempts.
    internal static async Task<string> CaptureAsync(IPage page, string key, CancellationToken cancellation)
    {
        ICDPSession? session = null;
        var objectGroup = "maliev-click-" + key;
        var samples = new List<object>();
        string? unavailable = null;
        var detached = false;
        var released = false;
        Task<ICDPSession>? creation = null;
        var lateSessionCleanupPending = false;
        var clock = Stopwatch.StartNew();
        try
        {
            creation = page.Context.NewCDPSessionAsync(page);
            session = await creation.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.ThrowIfCancellationRequested();
            var node = await SendAsync(session, "Runtime.evaluate", new()
            {
                ["expression"] = "window.__malievClickTrajectories?.[" + JsonSerializer.Serialize(key) + "]?.control",
                ["objectGroup"] = objectGroup,
                ["returnByValue"] = false,
                ["generatePreview"] = false,
                ["silent"] = true
            });
            var objectId = node.GetProperty("result").GetProperty("objectId").GetString()
                ?? throw new InvalidOperationException("Diagnostic node unavailable");
            long lastSequence = -1;
            // Bounded polling reads only numeric geometry. It never scrolls,
            // focuses, dispatches input, or changes locator action settings.
            while (!cancellation.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(40) && samples.Count < 12)
            {
                var before = await ReadStateAsync(session, objectId, key);
                if (!before.GetProperty("currentDocument").GetBoolean()) break;
                var sequence = before.GetProperty("scrollSequence").GetInt64();
                if (sequence != lastSequence)
                {
                    var protocol = await SendAsync(session, "DOM.getContentQuads", new() { ["objectId"] = objectId });
                    var after = await ReadStateAsync(session, objectId, key);
                    var stable = SameGeometry(before, after);
                    var quads = protocol.GetProperty("quads").EnumerateArray().Take(4)
                        .Select(quad => quad.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
                    var quadCount = protocol.GetProperty("quads").GetArrayLength();
                    // Retain instability explicitly; do not correlate a changed
                    // document/scroll/rect with a candidate from another sample.
                    samples.Add(new
                    {
                        sampleOrdinal = samples.Count,
                        before,
                        after,
                        bracketingGeometryEqual = stable,
                        protocolQuadCount = quadCount,
                        protocolQuads = quads,
                        candidatePoint = stable ? CandidatePoint(quads, before) : null,
                        candidateIncomplete = quadCount > quads.Length
                    });
                    lastSequence = sequence;
                }
                await Task.Delay(400, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception failure) { unavailable = failure.GetType().Name; }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await SendAsync(session, "Runtime.releaseObjectGroup", new() { ["objectGroup"] = objectGroup });
                    released = true;
                }
                catch (Exception failure) { unavailable ??= "release:" + failure.GetType().Name; }
                try { await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(2)); detached = true; }
                catch (Exception failure) { unavailable ??= "detach:" + failure.GetType().Name; }
            }
            else if (creation is not null)
            {
                // NewCDPSessionAsync has no cancellation API. Observe a late
                // result and detach it; the test's await-using context also owns
                // and closes that target. Report pending cleanup, never success.
                lateSessionCleanupPending = !creation.IsCompleted;
                _ = DetachLateSessionAsync(creation);
            }
        }
        var value = new
        {
            source = "read-only DOM.getContentQuads",
            correlation = "same element/document; equal bracketing scroll sequence and geometry do not prove stability between reads; retry ordinal unavailable",
            pointMeaning = "candidate from viewport-clipped protocol quad; not actual Playwright action or dispatched input",
            elapsedMilliseconds = clock.ElapsedMilliseconds,
            unavailable,
            released,
            detached,
            lateSessionCleanupPending,
            observedSamples = samples.Count,
            samples
        };
        var result = JsonSerializer.Serialize(value);
        while (result.Length > 8192 && samples.Count > 0)
        {
            samples.RemoveAt(0);
            result = JsonSerializer.Serialize(value);
        }
        return result.Length <= 8192 ? result : JsonSerializer.Serialize(new { unavailable = "bounded protocol output exceeded", released, detached, lateSessionCleanupPending });
    }

    private static async Task DetachLateSessionAsync(Task<ICDPSession> creation)
    {
        try { await (await creation).DetachAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* The owned browser context closes the target; no exception text is exported. */ }
    }

    private static async Task<JsonElement> SendAsync(ICDPSession session, string method, Dictionary<string, object> arguments)
    {
        var response = await session.SendAsync(method, arguments).WaitAsync(TimeSpan.FromSeconds(2));
        return response ?? throw new InvalidOperationException("Protocol response unavailable");
    }

    private static async Task<JsonElement> ReadStateAsync(ICDPSession session, string objectId, string key)
    {
        var response = await SendAsync(session, "Runtime.callFunctionOn", new()
        {
            ["objectId"] = objectId,
            ["arguments"] = new[] { new { value = key } },
            ["returnByValue"] = true,
            ["generatePreview"] = false,
            ["silent"] = true,
            ["functionDeclaration"] = """
                function(key) {
                    const state = window.__malievClickTrajectories?.[key];
                    const currentDocument = !!state && !state.stopped && state.control === this &&
                        this.isConnected && this.ownerDocument === document && state.timeOrigin === performance.timeOrigin;
                    const rect = this.getBoundingClientRect();
                    const main = this.closest('.legacy-main-content');
                    const mr = main?.getBoundingClientRect();
                    const visual = window.visualViewport;
                    return { currentDocument, timeOrigin: performance.timeOrigin, performanceNow: performance.now(),
                        scrollSequence: state?.scrollSequence ?? -1,
                        viewportWidth: innerWidth, viewportHeight: innerHeight,
                        rect: [rect.x, rect.y, rect.width, rect.height],
                        mainRect: mr ? [mr.x, mr.y, mr.width, mr.height] : null,
                        mainScrollTop: main?.scrollTop ?? null,
                        mainScrollHeight: main?.scrollHeight ?? null,
                        mainClientHeight: main?.clientHeight ?? null,
                        documentScrollTop: document.documentElement.scrollTop, bodyScrollTop: document.body.scrollTop,
                        visual: visual ? [visual.width, visual.height, visual.offsetLeft, visual.offsetTop, visual.scale] : null };
                }
                """
        });
        return response.GetProperty("result").GetProperty("value").Clone();
    }

    private static bool SameGeometry(JsonElement before, JsonElement after) =>
        after.GetProperty("currentDocument").GetBoolean() &&
        new[] { "timeOrigin", "scrollSequence", "viewportWidth", "viewportHeight", "rect", "mainRect",
            "mainScrollTop", "mainScrollHeight", "mainClientHeight", "documentScrollTop", "bodyScrollTop", "visual" }
            .All(name => before.GetProperty(name).GetRawText() == after.GetProperty(name).GetRawText());

    private static double[]? CandidatePoint(double[][] quads, JsonElement state)
    {
        var width = state.GetProperty("viewportWidth").GetDouble();
        var height = state.GetProperty("viewportHeight").GetDouble();
        foreach (var quad in quads)
        {
            if (quad.Length != 8 || quad.Any(value => !double.IsFinite(value))) continue;
            var clipped = quad.Select((value, index) => Math.Clamp(value, 0, index % 2 == 0 ? width : height)).ToArray();
            double area = 0;
            for (var index = 0; index < 4; index++)
            {
                var next = (index + 1) % 4;
                area += clipped[index * 2] * clipped[next * 2 + 1] - clipped[next * 2] * clipped[index * 2 + 1];
            }
            if (Math.Abs(area) / 2 > 0.99)
                return [Enumerable.Range(0, 4).Average(index => clipped[index * 2]),
                    Enumerable.Range(0, 4).Average(index => clipped[index * 2 + 1])];
        }
        return null;
    }
}
