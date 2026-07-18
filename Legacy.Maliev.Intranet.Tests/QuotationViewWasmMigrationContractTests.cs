extern alias Bff;

using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using CatalogMaterialsProxy = Bff::Legacy.Maliev.Intranet.Bff.Catalog.CatalogMaterialsProxy;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using EmployeesProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeesProxy;
using InvoicesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoicesProxy;
using QuotationDetailAggregator = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationDetailAggregator;
using QuotationFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationFileProxy;
using QuotationsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationViewWasmMigrationContractTests
{
    [Fact]
    public async Task Aggregator_PreservesOwnedReadContractsAndResolvesCleanFiles()
    {
        var quotation = new RoutingHandler(new Dictionary<string, string>
        {
            ["/quotations/84"] = """{"Id":84,"CustomerId":42,"EmployeeId":7,"InvoiceId":9,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"QuotedAmount":1040.27,"CurrencyId":1,"Comment":"Thai fixture","Fob":"Bangkok","ShippedVia":"Courier","Terms":"Net 7","Accepted":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null}""",
            ["/quotations/84/orders"] = """[{"Id":3,"QuotationId":84,"OrderId":51,"CreatedDate":"2030-07-18T00:00:00Z"}]""",
            ["/quotations/84/files"] = """[{"Id":4,"QuotationId":84,"Bucket":"maliev.com","ObjectName":"quotations/84/quotation.pdf","CreatedDate":"2030-07-18T00:00:00Z"}]""",
        });
        var aggregator = new QuotationDetailAggregator(
            new QuotationsProxy(Client(quotation, "quotation")),
            new CustomersProxy(Client(new RoutingHandler(new Dictionary<string, string> { ["/customers/42"] = """{"Id":42,"FirstName":"Nat","LastName":"T","FullName":"Nat T","Email":"customer@example.com"}""" }), "customer")),
            new EmployeesProxy(Client(new RoutingHandler(new Dictionary<string, string> { ["/employees/7"] = """{"Id":7,"FirstName":"A","LastName":"B","FullName":"A B","Email":"employee@example.com"}""" }), "employee")),
            new CatalogMaterialsProxy(Client(new RoutingHandler(new Dictionary<string, string> { ["/Currencies"] = """[{"Id":1,"ShortName":"THB","LongName":"Thai baht"}]""" }), "catalog")),
            new InvoicesProxy(Client(new RoutingHandler(new Dictionary<string, string> { ["/invoices/9"] = """{"Id":9,"Number":"INV-9"}""" }), "accounting")),
            new QuotationFileProxy(Client(new RoutingHandler(new Dictionary<string, string> { ["/uploads/SignedUrl?bucket=maliev.com&objectName=quotations%2F84%2Fquotation.pdf"] = "\"https://storage.example/clean.pdf\"" }), "file")));

        var page = await aggregator.GetAsync(84, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal("Nat T", page.Customer?.FullName);
        Assert.Equal("A B", page.Employee?.FullName);
        Assert.Equal("THB", page.Currency.ShortName);
        Assert.Equal("INV-9", page.Invoice?.Number);
        Assert.Equal(51, Assert.Single(page.Orders).OrderId);
        Assert.Equal("https://storage.example/clean.pdf", Assert.Single(page.Files).Uri?.ToString());
        Assert.DoesNotContain(quotation.Paths, path => path.Contains("accepted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ViewRoute_IsLazyReadOnlyAndContainsNoDecisionMutation()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "View.razor"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("@page \"/Quotations/View\"", page, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/{id:int}", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationsRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CustomersRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.EmployeesRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CatalogCurrenciesRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationOrdersRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationFilesRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.FileUploadsRead", program, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMethod.Put", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Accepted =", page, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient Client(HttpMessageHandler handler, string host) => new(handler) { BaseAddress = new($"http://{host}/") };

    private sealed class RoutingHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            Paths.Add(path);
            return Task.FromResult(responses.TryGetValue(path, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
