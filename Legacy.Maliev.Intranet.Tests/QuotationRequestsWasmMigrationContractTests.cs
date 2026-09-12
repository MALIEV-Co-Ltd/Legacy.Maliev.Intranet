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
    public void QualificationContracts_PreserveEveryReceiptAndAuditField()
    {
        var journeyId = Guid.NewGuid();
        var changed = new DateTime(2030, 7, 18, 9, 30, 0, DateTimeKind.Utc);
        var transition = new QuotationQualificationEvent(9, "retry-key", "unreviewed", "qualified", 2, changed, "employee-7", "complete", 0, null, "reviewed");
        var receipt = new QuotationQualificationReceipt(84, journeyId, "request-84", "qualified", changed, 2, [transition]);
        var update = new QuotationQualificationUpdate("qualified", null, "complete", 0, null, "retry-key", 1);

        Assert.Equal((84, journeyId, "request-84", "qualified", changed, 2), (receipt.RequestId, receipt.JourneyId, receipt.TransactionId, receipt.State, receipt.StateChangedUtc, receipt.Version));
        Assert.Same(transition, Assert.Single(receipt.Events));
        Assert.Equal((9L, "retry-key", "unreviewed", "qualified", 2, changed, "employee-7", "complete", 0, null, "reviewed"), (transition.Id, transition.IdempotencyKey, transition.PreviousState, transition.State, transition.Version, transition.ChangedUtc, transition.ChangedBy, transition.Completeness, transition.DuplicateCount, transition.UnmatchedClassification, transition.Reason));
        Assert.Equal(("qualified", null, "complete", 0, null, "retry-key", 1), (update.State, update.Reason, update.Completeness, update.DuplicateCount, update.UnmatchedClassification, update.IdempotencyKey, update.ExpectedVersion));
    }

    [Fact]
    public void Routes_AreLazyBrowserSafeAndUseOwnedBffContracts()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequests", "Index.razor"));
        var view = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequests", "View.razor"));
        var mapper = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationRequestsEndpointMapper.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationRequestsProxy.cs"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("@page \"/QuotationRequests/Index\"", index, StringComparison.Ordinal);
        Assert.Contains("<OperationalDataTable", index, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs", index, StringComparison.Ordinal);
        Assert.Contains("/QuotationRequests/View?id=", index, StringComparison.Ordinal);
        Assert.Contains("ExpandQuotationRequest", index, StringComparison.Ordinal);
        Assert.Contains("DetailsAriaLabel", index, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableColumn", index, StringComparison.Ordinal);
        Assert.Contains("@page \"/QuotationRequests/View\"", view, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", view, StringComparison.Ordinal);
        Assert.Contains("/qualification-receipt", view, StringComparison.Ordinal);
        Assert.Contains("SaveQualificationAsync", view, StringComparison.Ordinal);
        Assert.Contains("QuotationQualificationReceipt", view, StringComparison.Ordinal);
        Assert.Contains("<EditForm", view, StringComparison.Ordinal);
        Assert.Contains("<QuotationInputField", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", view, StringComparison.Ordinal);
        Assert.Contains("X-Expected-Modified-Date", proxy, StringComparison.Ordinal);
        Assert.Contains("/qualification-receipt", proxy, StringComparison.Ordinal);
        Assert.Contains("UpdateQualificationAsync", mapper, StringComparison.Ordinal);
        Assert.Contains("/uploads/SignedUrl", proxy, StringComparison.Ordinal);
        Assert.Contains("file.RequestId != id", mapper, StringComparison.Ordinal);
        Assert.Contains("QuotationRequests/", app, StringComparison.Ordinal);
        Assert.Contains("Features.Quotations.wasm", clientProject, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationRequestsRead", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationRequestsUpdate", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationFilesRead", bffProgram, StringComparison.Ordinal);
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

        using var qualification = await proxy.UpdateQualificationAsync(84, new("qualified", null, "complete", 0, null, "stable-key", 2), CancellationToken.None);
        Assert.Equal("/quotationrequests/84/qualification", handler.Path);
        using var qualificationDocument = JsonDocument.Parse(Assert.IsType<string>(handler.Body));
        Assert.Equal("stable-key", qualificationDocument.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal(2, qualificationDocument.RootElement.GetProperty("expectedVersion").GetInt32());
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
