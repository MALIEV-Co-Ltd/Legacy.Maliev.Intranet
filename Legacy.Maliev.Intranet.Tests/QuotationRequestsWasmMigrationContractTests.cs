extern alias Bff;

using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using QuotationRequestsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestsWasmMigrationContractTests
{
    [Fact]
    public void Routes_AreLazyBrowserSafeAndUseOwnedBffContracts()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequests.razor"));
        var view = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequestView.razor"));
        var mapper = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationRequestsEndpointMapper.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationRequestsProxy.cs"));

        Assert.Contains("@page \"/QuotationRequests/Index\"", index, StringComparison.Ordinal);
        Assert.Contains("@page \"/QuotationRequests/View\"", view, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", view, StringComparison.Ordinal);
        Assert.Contains("X-Expected-Modified-Date", proxy, StringComparison.Ordinal);
        Assert.Contains("/uploads/SignedUrl", proxy, StringComparison.Ordinal);
        Assert.Contains("file.RequestId != id", mapper, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Proxy_ForwardsBoundedListAndOptimisticUpdateContracts()
    {
        var handler = new RecordingHandler();
        var proxy = new QuotationRequestsProxy(new HttpClient(handler) { BaseAddress = new("http://quotation/") });
        using var list = await proxy.GetPageAsync(QuotationRequestSort.RequestModifiedDate_Descending, "Thai fixture", 1, 250, CancellationToken.None);
        Assert.Equal("/quotationrequests?sort=RequestModifiedDate_Descending&search=Thai%20fixture&index=1&size=250", handler.Path);

        var modified = new DateTime(2030, 7, 18, 9, 30, 0, DateTimeKind.Utc);
        using var update = await proxy.UpdateAsync(84, new("A", "B", "a@example.com", "1", "TH", "MALIEV", "TAX", "message", "internal", true, modified), CancellationToken.None);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/quotationrequests/84", handler.Path);
        Assert.Equal(modified.ToString("O", System.Globalization.CultureInfo.InvariantCulture), handler.ExpectedModifiedDate);
        using var document = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        Assert.False(document.RootElement.TryGetProperty("modifiedDate", out _));
        Assert.True(document.RootElement.GetProperty("done").GetBoolean());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ExpectedModifiedDate { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.PathAndQuery;
            Method = request.Method;
            ExpectedModifiedDate = request.Headers.TryGetValues("X-Expected-Modified-Date", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
