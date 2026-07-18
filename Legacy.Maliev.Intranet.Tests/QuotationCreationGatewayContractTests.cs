extern alias Bff;

using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Quotations;
using QuotationCreationGateway = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationCreationGateway;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationCreationGatewayContractTests
{
    [Fact]
    public async Task RequiredCreates_UseExactRoutesJsonAndStableIdempotencyKeys()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/quotations" => Json("""{"id":77}""", HttpStatusCode.Created),
            "/quotations/orderitems" => Json("""{"id":101}""", HttpStatusCode.Created),
            "/quotations/77/orders/42" => Json("""{"id":201}""", HttpStatusCode.Created),
            "/orderstatuses/histories/42/quoted" => new(HttpStatusCode.NoContent),
            _ => new(HttpStatusCode.NotFound),
        });
        var gateway = new QuotationCreationGateway(new StubFactory(handler));
        var input = Request();
        var priced = QuotationPricing.Calculate(input, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        Assert.Equal(77, await gateway.CreateQuotationAsync(input, priced, "attempt:quotation", CancellationToken.None));
        Assert.Equal(101, await gateway.CreateLineAsync(77, priced.Lines[0], "attempt:line:0", CancellationToken.None));
        Assert.Equal(201, await gateway.CreateOrderLinkAsync(77, 42, "attempt:order-link:42", CancellationToken.None));
        await gateway.MarkOrderQuotedAsync(42, "attempt:order-status:42", CancellationToken.None);

        Assert.Equal(
            ["/quotations", "/quotations/orderitems", "/quotations/77/orders/42", "/orderstatuses/histories/42/quoted"],
            handler.Requests.Select(request => request.Path).ToArray());
        Assert.Equal(
            ["attempt:quotation", "attempt:line:0", "attempt:order-link:42", "attempt:order-status:42"],
            handler.Requests.Select(request => request.IdempotencyKey!).ToArray());
        Assert.Contains("\"Subtotal\":100", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"Total\":107", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"WithholdingTax\":0", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"QuotationId\":77", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"OrderId\":42", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizeDocumentDelivery_RendersUploadsLinksAndEmailsThePdfThroughServerClients()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/customers/3" => Json(CustomerJson),
            "/employees/2" => Json(EmployeeJson),
            "/Currencies" => Json("""[{"Id":1,"ShortName":"THB","LongName":"Thai Baht"}]"""),
            "/Countries" => Json("""[{"Id":764,"Name":"Thailand"}]"""),
            "/pdfs/quotation" => Pdf(),
            "/Uploads" => Json("""{"Object":[{"Bucket":"maliev.com","ObjectName":"quotations/2026/07/18/quotation.pdf","Uri":"https://storage.test/q"}]}""", HttpStatusCode.Created),
            "/quotations/77/files" => Json("""{"Id":301}""", HttpStatusCode.Created),
            "/Emails/manufacturing" => new(HttpStatusCode.OK),
            _ => new(HttpStatusCode.NotFound),
        });
        var gateway = new QuotationCreationGateway(new StubFactory(handler));
        var input = Request();
        var priced = QuotationPricing.Calculate(input, DateTimeOffset.Parse("2026-07-18T00:00:00Z"));

        await gateway.FinalizeDocumentDeliveryAsync(77, input, priced, CancellationToken.None);

        Assert.Equal(
            ["/customers/3", "/employees/2", "/Currencies", "/Countries", "/pdfs/quotation", "/Uploads", "/quotations/77/files", "/Emails/manufacturing"],
            handler.Requests.Select(request => request.Path).ToArray());
        Assert.Contains("น้ำ", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("Thailand", handler.Requests[4].Body, StringComparison.Ordinal);
        Assert.Contains("Quotation_77_18072026.pdf", handler.Requests[5].Body, StringComparison.Ordinal);
        Assert.Contains("bucket=maliev.com", handler.Requests[6].Query, StringComparison.Ordinal);
        Assert.Contains("Quotation%20%2377", handler.Requests[7].Query, StringComparison.Ordinal);
        Assert.Contains("Quotation_77_18072026.pdf", handler.Requests[7].Body, StringComparison.Ordinal);
    }

    private static QuotationCreateRequest Request() => new(
        3, 2, 1, 30, "Courier", "Bangkok", "Net 30", "น้ำ", false,
        [new(42, "น้ำ", 2, 50m, 0m)]);

    private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Pdf() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent("%PDF-fixture"u8.ToArray())
        {
            Headers = { ContentType = new("application/pdf") },
        },
    };

    private sealed class StubFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://legacy.test"),
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
                body));
            return response(request);
        }
    }

    private sealed record CapturedRequest(string Path, string Query, string? IdempotencyKey, string Body);

    private const string CustomerJson = """
        {"Id":3,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Telephone":"02","Mobile":null,"Fax":null,"Email":"ada@example.test","DateOfBirth":null,"CompanyId":4,"BillingAddressId":5,"ShippingAddressId":null,"CreatedDate":null,"ModifiedDate":null,"BillingAddress":{"Id":5,"Building":null,"AddressLine1":"36/1","AddressLine2":null,"City":"Pak Kret","State":"Nonthaburi","PostalCode":"11120","CountryId":764,"CreatedDate":null,"ModifiedDate":null},"Company":{"Id":4,"Name":"MALIEV","TaxNumber":"TAX","Registrar":"REG","CreatedDate":null,"ModifiedDate":null},"ShippingAddress":null}
        """;
    private const string EmployeeJson = """
        {"Id":2,"RoleId":1,"FirstName":"Grace","LastName":"Hopper","FullName":"Grace Hopper","PhoneNumber":"081","Email":"grace@example.test","DateOfBirth":null,"HomeAddressId":null,"CreatedDate":null,"ModifiedDate":null,"HomeAddress":null,"Role":null}
        """;
}
