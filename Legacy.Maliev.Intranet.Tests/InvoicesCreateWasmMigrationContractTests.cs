extern alias Bff;

using System.Net;
using System.Reflection;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Http;
using InvoiceCreationEndpointMapper = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceCreationEndpointMapper;
using InvoiceCreationProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceCreationProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoicesCreateWasmMigrationContractTests
{
    [Fact]
    public void InvoiceCreate_IsLazyWasmAndKeepsAuthorityOutOfBrowserRequest()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "InvoiceCreate.razor");
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".resx")));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Invoices/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("/bff/invoices/from-quotation/", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", page, StringComparison.Ordinal);
        Assert.Contains("Guid.NewGuid", page, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingCreate", program, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);

        var properties = typeof(CreateInvoiceFromQuotationRequest).GetProperties().Select(value => value.Name).ToArray();
        Assert.DoesNotContain("CustomerId", properties);
        Assert.DoesNotContain("SalesPerson", properties);
        Assert.DoesNotContain("Currency", properties);
        Assert.DoesNotContain("Subtotal", properties);
        Assert.DoesNotContain("Vat", properties);
        Assert.DoesNotContain("Total", properties);
        Assert.DoesNotContain("OrderItems", properties);
    }

    [Fact]
    public async Task Proxy_UsesExactAccountingRoutesAndForwardsStableUuid()
    {
        var handler = new CaptureHandler();
        var proxy = new InvoiceCreationProxy(new HttpClient(handler) { BaseAddress = new("http://accounting") });
        var operationId = Guid.Parse("4f7870e2-d349-41bb-b4cf-567450f261e9");

        using var preview = await proxy.PreviewAsync(84, CancellationToken.None);
        using var created = await proxy.CreateAsync(84, Request(), operationId, CancellationToken.None);

        Assert.Equal(["GET /invoices/from-quotation/84/preview", "POST /invoices/from-quotation/84"], handler.Requests);
        Assert.Equal(operationId.ToString("D"), handler.IdempotencyKey);
    }

    [Fact]
    public async Task Mapper_RejectsMissingStableUuidBeforeAccountingCall()
    {
        var handler = new CaptureHandler();
        var proxy = new InvoiceCreationProxy(new HttpClient(handler) { BaseAddress = new("http://accounting") });
        var context = new DefaultHttpContext();

        var result = await InvoiceCreationEndpointMapper.CreateAsync(84, Request(), context, proxy, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Empty(handler.Requests);
    }

    private static CreateInvoiceFromQuotationRequest Request() => new("INV-84", null, null, null, null, null, null, new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, true);
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string? IdempotencyKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Headers.TryGetValues("Idempotency-Key", out var values)) IdempotencyKey = values.Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
