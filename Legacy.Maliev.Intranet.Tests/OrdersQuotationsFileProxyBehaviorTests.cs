extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersQuotationsFileProxyBehaviorTests
{
    [Fact]
    public async Task DurableUpload_ForwardsExactMultipartPathIdentityAndSafeFilename()
    {
        var handler = new RecordingHandler();
        var proxy = new OrderFileProxy(new HttpClient(handler) { BaseAddress = new("https://file.test") });
        var upload = FormFile("STEP", "../drawing.step", "model/step");

        using var response = await proxy.UploadAsync(42, [upload], "orders/42/stable upload", "attempt-123", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/Uploads?bucket=maliev.com&path=orders%2F42%2Fstable%20upload", request.PathAndQuery);
        Assert.Equal("attempt-123", request.IdempotencyKey);
        Assert.Contains("name=files", request.Body, StringComparison.Ordinal);
        Assert.Contains("filename=drawing.step", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("..", request.Body, StringComparison.Ordinal);
        Assert.Contains("model/step", request.Body, StringComparison.Ordinal);
        Assert.Contains("STEP", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedUrlAndDelete_EscapeServerResolvedCoordinatesAndUseExactMethods()
    {
        var handler = new RecordingHandler();
        var proxy = new OrderFileProxy(new HttpClient(handler) { BaseAddress = new("https://file.test") });

        using var signed = await proxy.GetSignedUrlAsync("maliev.com", "orders/42/ชิ้นงาน.step", CancellationToken.None);
        using var deleted = await proxy.DeleteAsync("maliev.com", "orders/42/ชิ้นงาน.step", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, signed.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, deleted.StatusCode);
        Assert.Collection(
            handler.Requests,
            item =>
            {
                Assert.Equal("GET", item.Method);
                Assert.Equal("/uploads/SignedUrl?bucket=maliev.com&objectName=orders%2F42%2F%E0%B8%8A%E0%B8%B4%E0%B9%89%E0%B8%99%E0%B8%87%E0%B8%B2%E0%B8%99.step", item.PathAndQuery);
            },
            item =>
            {
                Assert.Equal("DELETE", item.Method);
                Assert.Equal("/Uploads?bucket=maliev.com&objectName=orders%2F42%2F%E0%B8%8A%E0%B8%B4%E0%B9%89%E0%B8%99%E0%B8%87%E0%B8%B2%E0%B8%99.step", item.PathAndQuery);
            });
    }

    private static FormFile FormFile(string value, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new(
                request.Method.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new(HttpStatusCode.Accepted);
        }
    }

    private sealed record RecordedRequest(string Method, string PathAndQuery, string? IdempotencyKey, string? Body);
}
