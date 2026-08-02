extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.PurchaseOrders;
using PurchaseOrderCreationGateway = Bff::Legacy.Maliev.Intranet.Bff.Procurement.PurchaseOrderCreationGateway;
using PurchaseOrderDetailGateway = Bff::Legacy.Maliev.Intranet.Bff.Procurement.PurchaseOrderDetailGateway;
using SupplierManagementClient = Bff::Legacy.Maliev.Intranet.Bff.Procurement.SupplierManagementClient;

namespace Legacy.Maliev.Intranet.Tests;

internal static class TestContext
{
    internal static TestCancellationContext Current { get; } = new();
}

internal sealed class TestCancellationContext
{
    internal CancellationToken CancellationToken => CancellationToken.None;
}

public sealed class ProcurementStateGatewayBehaviorTests
{
    private const string ServiceAuthorization = "Bearer signed-service-token";

    [Fact]
    public async Task CreationGateway_UsesExactOptionRoutesAndFiltersInvalidRows()
    {
        var factory = new RecordingClientFactory((name, request) => Task.FromResult(
            request.PathAndQuery switch
            {
                "/Suppliers?sort=SupplierName_Ascending&search=&index=1&size=250" => Json("{\"items\":[{\"id\":7,\"name\":\"Supplier A\"},{\"id\":0,\"name\":\"Invalid\"}]}"),
                "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250" => Json("{\"items\":[{\"id\":9,\"fullName\":\"Employee A\"},{\"id\":10,\"fullName\":\"\"}]}"),
                "/purchaseorders/addresses" => Json("[{\"id\":11,\"addressLine1\":\"16/1\",\"city\":\"Nonthaburi\",\"countryId\":218},{\"id\":12,\"addressLine1\":\"\",\"countryId\":218}]"),
                _ => throw new InvalidOperationException($"Unexpected {name} request: {request.PathAndQuery}"),
            }));

        var result = await new PurchaseOrderCreationGateway(factory).GetOptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new PurchaseOrderSupplierOption(7, "Supplier A"), Assert.Single(result.Suppliers));
        Assert.Equal(new PurchaseOrderEmployeeOption(9, "Employee A"), Assert.Single(result.Employees));
        Assert.Equal(new PurchaseOrderAddressOption(11, "16/1", "Nonthaburi"), Assert.Single(result.Addresses));
        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/Suppliers?sort=SupplierName_Ascending&search=&index=1&size=250"),
            (PurchaseOrderCreationGateway.EmployeeClient, HttpMethod.Get, "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/addresses"));
    }

    [Fact]
    public async Task CreationGateway_EmitsExactOrderAndItemContractsWithReplayKeys()
    {
        var factory = new RecordingClientFactory((_, request) => Task.FromResult(
            request.PathAndQuery == "/PurchaseOrders"
                ? Json("{\"id\":41,\"createdDate\":\"2026-08-02T10:30:00Z\"}")
                : Json("{\"id\":73}")));
        var gateway = new PurchaseOrderCreationGateway(factory);
        var request = CreateOrderRequest();

        var created = await gateway.CreateOrderAsync(request, "attempt-1", TestContext.Current.CancellationToken);
        var firstItem = await gateway.CreateItemAsync(41, request.Items[0], "attempt-1", 0, TestContext.Current.CancellationToken);
        var repeatedItem = await gateway.CreateItemAsync(41, request.Items[0], "attempt-1", 0, TestContext.Current.CancellationToken);
        var secondItem = await gateway.CreateItemAsync(41, request.Items[0], "attempt-1", 1, TestContext.Current.CancellationToken);

        Assert.Equal(41, created.Id);
        Assert.Equal(73, firstItem);
        Assert.Equal(73, repeatedItem);
        Assert.Equal(73, secondItem);
        var order = factory.Requests.Single(value => value.PathAndQuery == "/PurchaseOrders");
        Assert.Equal(PurchaseOrderCreationGateway.ProcurementClient, order.ClientName);
        Assert.Equal(HttpMethod.Post, order.Method);
        Assert.StartsWith("application/json", order.ContentType, StringComparison.Ordinal);
        Assert.Equal(ServiceAuthorization, order.Authorization);
        Assert.Equal("attempt-1", order.IdempotencyKey);
        AssertJson(order.Body!, new Dictionary<string, object?>
        {
            ["supplierId"] = 7L,
            ["supplierContactPerson"] = "Purchasing",
            ["shippingAddressId"] = 11L,
            ["shippingContactPerson"] = "Ship",
            ["shippingTelephone"] = "0201",
            ["shippingMobile"] = "0690",
            ["shippingFax"] = "0202",
            ["billingAddressId"] = 12L,
            ["billingContactPerson"] = "Bill",
            ["billingTelephone"] = "0301",
            ["billingMobile"] = "0404",
            ["billingFax"] = "0302",
            ["fob"] = "Bangkok",
            ["terms"] = "30 days",
            ["shippingMethod"] = "Truck",
            ["employeeId"] = 9L,
            ["notes"] = "Handle carefully",
        });
        var items = factory.Requests.Where(value => value.PathAndQuery == "/purchaseorders/orderitems").ToArray();
        Assert.Equal(3, items.Length);
        Assert.All(items, item =>
        {
            Assert.Equal(PurchaseOrderCreationGateway.ProcurementClient, item.ClientName);
            Assert.Equal(HttpMethod.Post, item.Method);
            Assert.StartsWith("application/json", item.ContentType, StringComparison.Ordinal);
            Assert.Equal(ServiceAuthorization, item.Authorization);
        });
        Assert.Equal(items[0].IdempotencyKey, items[1].IdempotencyKey);
        Assert.NotEqual(items[0].IdempotencyKey, items[2].IdempotencyKey);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", items[0].IdempotencyKey!);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", items[2].IdempotencyKey!);
        AssertJson(items[0].Body!, new Dictionary<string, object?>
        {
            ["purchaseOrderId"] = 41L,
            ["partNumber"] = "PN-1",
            ["description"] = "Part",
            ["quantity"] = 2L,
            ["unitPrice"] = 125.50m,
        });
    }

    [Fact]
    public async Task CreationGateway_LoadsDocumentReferencesFromEveryExactOwnerRoute()
    {
        var factory = new RecordingClientFactory((_, request) => Task.FromResult(request.PathAndQuery switch
        {
            "/Suppliers/7" => Json("{\"id\":7,\"name\":\"Supplier A\",\"telephone\":\"0201\",\"mobile\":\"0690\",\"fax\":\"0202\"}"),
            "/suppliers/7/addresses" => Json("{\"id\":17,\"address1\":\"1 Supplier Road\",\"city\":\"Bangkok\",\"countryId\":218}"),
            "/purchaseorders/addresses/11" => Json("{\"id\":11,\"addressLine1\":\"16/1 Shipping Road\",\"city\":\"Nonthaburi\",\"countryId\":218}"),
            "/purchaseorders/addresses/12" => Json("{\"id\":12,\"addressLine1\":\"16/1 Billing Road\",\"city\":\"Nonthaburi\",\"countryId\":218}"),
            "/employees/9" => Json("{\"id\":9,\"fullName\":\"Employee A\"}"),
            "/Countries" => Json("[{\"id\":218,\"name\":\"Thailand\"},{\"id\":0,\"name\":\"Invalid\"}]"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}"),
        }));

        var result = await new PurchaseOrderCreationGateway(factory)
            .GetDocumentReferencesAsync(CreateOrderRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("Supplier A", result.Supplier.CompanyName);
        Assert.Equal("1 Supplier Road", result.Supplier.Address.AddressLine1);
        Assert.Equal("16/1 Shipping Road", result.ShippingAddress.AddressLine1);
        Assert.Equal("16/1 Billing Road", result.BillingAddress.AddressLine1);
        Assert.Equal("Employee A", result.EmployeeFullName);
        Assert.Equal("Thailand", Assert.Single(result.Countries).Value);
        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/Suppliers/7"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/suppliers/7/addresses"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/addresses/11"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/addresses/12"),
            (PurchaseOrderCreationGateway.EmployeeClient, HttpMethod.Get, "/employees/9"),
            (PurchaseOrderCreationGateway.CatalogClient, HttpMethod.Get, "/Countries"));
    }

    [Fact]
    public async Task CreationGateway_RendersUploadsLinksAndCompensatesUsingExactBoundaries()
    {
        var factory = new RecordingClientFactory((_, request) => Task.FromResult(request.PathAndQuery switch
        {
            "/Pdfs/purchaseorder" => Binary([1, 2, 3], "application/pdf"),
            "/Uploads?bucket=maliev.com&path=purchaseorders%2F41" => Json("{\"object\":[{\"bucket\":\"maliev.com\",\"objectName\":\"purchaseorders/41/order.pdf\"}]}"),
            "/purchaseorders/41/files?bucket=maliev.com&objectName=purchaseorders%2F41%2Forder.pdf" => Json("{\"id\":88}"),
            _ when request.Method == HttpMethod.Delete => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}"),
        }));
        var gateway = new PurchaseOrderCreationGateway(factory);
        var pdfDocument = CreatePdfDocument();

        Assert.Equal([1, 2, 3], await gateway.RenderPdfAsync(pdfDocument, TestContext.Current.CancellationToken));
        var stored = await gateway.UploadPdfAsync(41, [1, 2, 3], "attempt-1", TestContext.Current.CancellationToken);
        var repeatedStored = await gateway.UploadPdfAsync(41, [1, 2, 3], "attempt-1", TestContext.Current.CancellationToken);
        Assert.Equal(stored, repeatedStored);
        Assert.Equal(88, await gateway.LinkFileAsync(41, stored, "attempt-1", TestContext.Current.CancellationToken));
        Assert.Equal(88, await gateway.LinkFileAsync(41, stored, "attempt-1", TestContext.Current.CancellationToken));
        await gateway.DeleteFileLinkAsync(88, TestContext.Current.CancellationToken);
        await gateway.DeleteStoredFileAsync(stored, TestContext.Current.CancellationToken);
        await gateway.DeleteItemAsync(73, TestContext.Current.CancellationToken);
        await gateway.DeleteOrderAsync(41, TestContext.Current.CancellationToken);

        var render = factory.Requests.Single(value => value.PathAndQuery == "/Pdfs/purchaseorder");
        Assert.Equal(PurchaseOrderCreationGateway.DocumentClient, render.ClientName);
        Assert.Equal(HttpMethod.Post, render.Method);
        Assert.StartsWith("application/json", render.ContentType, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(render.Body!))
        {
            Assert.Equal("Bangkok", json.RootElement.GetProperty("FOB").GetString());
            Assert.False(json.RootElement.TryGetProperty("fob", out _));
        }
        var uploads = factory.Requests.Where(value => value.PathAndQuery.StartsWith("/Uploads?bucket=maliev.com&path=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, uploads.Length);
        Assert.All(uploads, upload =>
        {
            Assert.Equal(PurchaseOrderCreationGateway.FileClient, upload.ClientName);
            Assert.Equal(HttpMethod.Post, upload.Method);
            Assert.StartsWith("multipart/form-data; boundary=", upload.ContentType, StringComparison.Ordinal);
            Assert.Contains("name=files", upload.Body!, StringComparison.Ordinal);
            Assert.Contains("filename=PurchaseOrder_41.pdf", upload.Body!, StringComparison.Ordinal);
            Assert.Contains("Content-Type: application/pdf", upload.Body!, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(uploads[0].IdempotencyKey, uploads[1].IdempotencyKey);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", uploads[0].IdempotencyKey!);

        var links = factory.Requests.Where(value => value.PathAndQuery.StartsWith("/purchaseorders/41/files?", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, links.Length);
        Assert.All(links, link =>
        {
            Assert.Equal(PurchaseOrderCreationGateway.ProcurementClient, link.ClientName);
            Assert.Equal(HttpMethod.Post, link.Method);
            Assert.Null(link.ContentType);
        });
        Assert.Equal(links[0].IdempotencyKey, links[1].IdempotencyKey);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", links[0].IdempotencyKey!);
        Assert.NotEqual(uploads[0].IdempotencyKey, links[0].IdempotencyKey);
        Assert.All(factory.Requests, value => Assert.Equal(ServiceAuthorization, value.Authorization));

        AssertRequests(
            factory.Requests.Where(value => value.Method == HttpMethod.Delete),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/purchaseorders/files/88"),
            (PurchaseOrderCreationGateway.FileClient, HttpMethod.Delete, "/Uploads?bucket=maliev.com&objectName=purchaseorders%2F41%2Forder.pdf"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/purchaseorders/orderitems/73"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/PurchaseOrders/41"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PurchaseOrderCreationStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, PurchaseOrderCreationStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PurchaseOrderCreationStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, PurchaseOrderCreationStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, PurchaseOrderCreationStatus.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, PurchaseOrderCreationStatus.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PurchaseOrderCreationStatus.Unavailable)]
    [InlineData(HttpStatusCode.UnprocessableEntity, PurchaseOrderCreationStatus.BadGateway)]
    public async Task CreationGateway_MapsDownstreamFailures(HttpStatusCode statusCode, PurchaseOrderCreationStatus expected)
    {
        var factory = new RecordingClientFactory((_, _) =>
        {
            var response = new HttpResponseMessage(statusCode);
            if (statusCode == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(12));
            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<PurchaseOrderGatewayException>(() =>
            new PurchaseOrderCreationGateway(factory).CreateOrderAsync(CreateOrderRequest(), "attempt", TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Status);
        Assert.Equal(statusCode == HttpStatusCode.TooManyRequests ? TimeSpan.FromSeconds(12) : (TimeSpan?)null, exception.RetryAfter);
        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Post, "/PurchaseOrders"));
    }

    [Fact]
    public async Task DetailGateway_UsesExactReadDeleteAndSignedUrlContracts()
    {
        var factory = new RecordingClientFactory((_, request) => Task.FromResult(request.PathAndQuery switch
        {
            "/PurchaseOrders/41" => Json("{\"id\":41,\"supplierId\":7,\"employeeId\":9}"),
            "/Suppliers/7" => Json("{\"name\":\"Supplier A\"}"),
            "/employees/9" => Json("{\"fullName\":\"Employee A\"}"),
            "/purchaseorders/41/orderitems" => Json("[{\"id\":73,\"purchaseOrderId\":41,\"description\":\"Part\"}]"),
            "/purchaseorders/41/files" => Json("[{\"id\":88,\"purchaseOrderId\":41,\"bucket\":\"maliev.com\",\"objectName\":\"purchaseorders/41/order.pdf\"}]"),
            "/uploads/SignedUrl?bucket=maliev.com&objectName=purchaseorders%2F41%2Forder.pdf" => Json("\"https://storage.example.test/signed\""),
            _ when request.Method == HttpMethod.Delete => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}"),
        }));
        var gateway = new PurchaseOrderDetailGateway(factory);

        Assert.Equal(41, (await gateway.GetOrderAsync(41, TestContext.Current.CancellationToken))!.Id);
        Assert.Equal("Supplier A", await gateway.GetSupplierNameAsync(7, TestContext.Current.CancellationToken));
        Assert.Equal("Employee A", await gateway.GetEmployeeNameAsync(9, TestContext.Current.CancellationToken));
        Assert.Equal(73, Assert.Single(await gateway.GetItemsAsync(41, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(88, Assert.Single(await gateway.GetFilesAsync(41, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(new Uri("https://storage.example.test/signed"), await gateway.GetSignedUrlAsync("maliev.com", "purchaseorders/41/order.pdf", TestContext.Current.CancellationToken));
        await gateway.DeleteStoredFileAsync("maliev.com", "purchaseorders/41/order.pdf", TestContext.Current.CancellationToken);
        await gateway.DeleteFileLinkAsync(88, TestContext.Current.CancellationToken);
        await gateway.DeleteItemAsync(73, TestContext.Current.CancellationToken);
        await gateway.DeleteOrderAsync(41, TestContext.Current.CancellationToken);

        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/PurchaseOrders/41"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/Suppliers/7"),
            (PurchaseOrderCreationGateway.EmployeeClient, HttpMethod.Get, "/employees/9"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/41/orderitems"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/41/files"),
            (PurchaseOrderCreationGateway.FileClient, HttpMethod.Get, "/uploads/SignedUrl?bucket=maliev.com&objectName=purchaseorders%2F41%2Forder.pdf"),
            (PurchaseOrderCreationGateway.FileClient, HttpMethod.Delete, "/Uploads?bucket=maliev.com&objectName=purchaseorders%2F41%2Forder.pdf"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/purchaseorders/files/88"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/purchaseorders/orderitems/73"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Delete, "/PurchaseOrders/41"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, PurchaseOrderDetailStatus.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized, PurchaseOrderDetailStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PurchaseOrderDetailStatus.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, PurchaseOrderDetailStatus.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PurchaseOrderDetailStatus.Unavailable)]
    [InlineData(HttpStatusCode.Conflict, PurchaseOrderDetailStatus.BadGateway)]
    public async Task DetailGateway_MapsDownstreamFailures(HttpStatusCode statusCode, PurchaseOrderDetailStatus expected)
    {
        var factory = new RecordingClientFactory((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));

        var exception = await Assert.ThrowsAsync<PurchaseOrderDetailGatewayException>(() =>
            new PurchaseOrderDetailGateway(factory).GetOrderAsync(41, TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Status);
    }

    [Fact]
    public async Task DetailGateway_ReturnsContractDefaultsForNotFoundResources()
    {
        var factory = new RecordingClientFactory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var gateway = new PurchaseOrderDetailGateway(factory);

        Assert.Null(await gateway.GetOrderAsync(41, TestContext.Current.CancellationToken));
        Assert.Null(await gateway.GetSupplierNameAsync(7, TestContext.Current.CancellationToken));
        Assert.Null(await gateway.GetEmployeeNameAsync(9, TestContext.Current.CancellationToken));
        Assert.Empty(await gateway.GetItemsAsync(41, TestContext.Current.CancellationToken));
        Assert.Empty(await gateway.GetFilesAsync(41, TestContext.Current.CancellationToken));
        Assert.Null(await gateway.GetSignedUrlAsync("maliev.com", "missing.pdf", TestContext.Current.CancellationToken));
        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/PurchaseOrders/41"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/Suppliers/7"),
            (PurchaseOrderCreationGateway.EmployeeClient, HttpMethod.Get, "/employees/9"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/41/orderitems"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/purchaseorders/41/files"),
            (PurchaseOrderCreationGateway.FileClient, HttpMethod.Get, "/uploads/SignedUrl?bucket=maliev.com&objectName=missing.pdf"));
    }

    [Fact]
    public async Task Gateways_FailClosedWhenSuccessfulJsonIsMalformed()
    {
        var factory = new RecordingClientFactory((_, _) => Task.FromResult(Json("{invalid")));

        var creation = await Assert.ThrowsAsync<PurchaseOrderGatewayException>(() =>
            new PurchaseOrderCreationGateway(factory).CreateOrderAsync(CreateOrderRequest(), "attempt", TestContext.Current.CancellationToken));
        var detail = await Assert.ThrowsAsync<PurchaseOrderDetailGatewayException>(() =>
            new PurchaseOrderDetailGateway(factory).GetOrderAsync(41, TestContext.Current.CancellationToken));

        Assert.Equal(PurchaseOrderCreationStatus.BadGateway, creation.Status);
        Assert.Equal(PurchaseOrderDetailStatus.BadGateway, detail.Status);
        AssertRequests(factory.Requests,
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Post, "/PurchaseOrders"),
            (PurchaseOrderCreationGateway.ProcurementClient, HttpMethod.Get, "/PurchaseOrders/41"));
    }

    [Fact]
    public async Task SupplierClient_PreservesExactRoutesBodiesAndResponseStatus()
    {
        var handler = new RecordingHandler("supplier", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://supplier.test") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "signed-service-token");
        var client = new SupplierManagementClient(http);
        var request = new SupplierCreateRequest
        {
            Name = "Supplier A",
            Website = "https://supplier.test",
            TaxNumber = "TAX",
            Email = "buy@supplier.test",
            Note = "Preferred",
            Telephone = "0201",
            Mobile = "0690",
            Fax = "0202",
            Building = "Factory",
            Address1 = "1 Road",
            Address2 = "Unit 2",
            City = "Bangkok",
            State = "Bangkok",
            PostalCode = "10110",
            CountryId = 218,
        };

        using var profile = await client.GetProfileAsync(7, TestContext.Current.CancellationToken);
        using var address = await client.GetAddressAsync(7, TestContext.Current.CancellationToken);
        using var update = await client.UpdateProfileAsync(7, request, TestContext.Current.CancellationToken);
        using var createAddress = await client.CreateAddressAsync(7, request, TestContext.Current.CancellationToken);
        using var updateAddress = await client.UpdateAddressAsync(17, request, TestContext.Current.CancellationToken);
        using var delete = await client.DeleteProfileAsync(7, TestContext.Current.CancellationToken);

        Assert.All(new[] { profile, address, update, createAddress, updateAddress, delete }, value => Assert.Equal(HttpStatusCode.Accepted, value.StatusCode));
        Assert.All(handler.Requests, value => Assert.Equal(ServiceAuthorization, value.Authorization));
        Assert.Equal(
            ["/Suppliers/7", "/suppliers/7/addresses", "/Suppliers/7", "/suppliers/7/addresses", "/suppliers/addresses/17", "/Suppliers/7"],
            handler.Requests.Select(value => value.PathAndQuery));
        Assert.Equal([HttpMethod.Get, HttpMethod.Get, HttpMethod.Put, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete], handler.Requests.Select(value => value.Method));
        Assert.Null(handler.Requests[0].ContentType);
        Assert.Null(handler.Requests[1].ContentType);
        Assert.StartsWith("application/json", handler.Requests[2].ContentType, StringComparison.Ordinal);
        Assert.StartsWith("application/json", handler.Requests[3].ContentType, StringComparison.Ordinal);
        Assert.StartsWith("application/json", handler.Requests[4].ContentType, StringComparison.Ordinal);
        Assert.Null(handler.Requests[5].ContentType);
        AssertJson(handler.Requests[2].Body!, new Dictionary<string, object?>
        {
            ["name"] = "Supplier A",
            ["website"] = "https://supplier.test",
            ["taxNumber"] = "TAX",
            ["email"] = "buy@supplier.test",
            ["note"] = "Preferred",
            ["telephone"] = "0201",
            ["mobile"] = "0690",
            ["fax"] = "0202",
        });
        AssertJson(handler.Requests[3].Body!, new Dictionary<string, object?>
        {
            ["building"] = "Factory",
            ["address1"] = "1 Road",
            ["address2"] = "Unit 2",
            ["city"] = "Bangkok",
            ["state"] = "Bangkok",
            ["postalCode"] = "10110",
            ["countryId"] = 218L,
        });
    }

    private static PurchaseOrderCreateRequest CreateOrderRequest() => new()
    {
        SupplierId = 7,
        SupplierContactPerson = "Purchasing",
        ShippingAddressId = 11,
        ShippingContactPerson = "Ship",
        ShippingTelephone = "0201",
        ShippingMobile = "0690",
        ShippingFax = "0202",
        BillingAddressId = 12,
        BillingContactPerson = "Bill",
        BillingTelephone = "0301",
        BillingMobile = "0404",
        BillingFax = "0302",
        Fob = "Bangkok",
        Terms = "30 days",
        ShippingMethod = "Truck",
        EmployeeId = 9,
        Notes = "Handle carefully",
        Items = [new() { PartNumber = "PN-1", Description = "Part", Quantity = 2, UnitPrice = 125.50m }],
    };

    private static PurchaseOrderPdfDocument CreatePdfDocument()
    {
        var address = new PurchaseOrderPdfAddress("1 Road", null, null, "Bangkok", "Thailand", "10110", "Bangkok");
        var party = new PurchaseOrderPdfParty(address, "MALIEV", "Contact", null, "0690", "0201");
        return new(party, new DateTime(2026, 8, 2), "Bangkok", "Notes", "Employee A",
            [new("THB", "Part", "PN-1", 2, 251m, 125.50m)], 41, "Truck", party, party, "30 days");
    }

    private static void AssertJson(string json, IReadOnlyDictionary<string, object?> expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected.Keys.Order(), document.RootElement.EnumerateObject().Select(value => value.Name).Order());
        foreach (var (name, value) in expected)
        {
            var property = document.RootElement.GetProperty(name);
            if (value is long integer) Assert.Equal(integer, property.GetInt64());
            else if (value is decimal number) Assert.Equal(number, property.GetDecimal());
            else Assert.Equal(value, property.ValueKind == JsonValueKind.Null ? null : property.GetString());
        }
    }

    private static void AssertRequests(
        IEnumerable<RequestSnapshot> requests,
        params (string ClientName, HttpMethod Method, string PathAndQuery)[] expected)
    {
        var actual = requests.ToArray();
        Assert.Equal(
            expected.Select(value => $"{value.ClientName}|{value.Method}|{value.PathAndQuery}").Order(StringComparer.Ordinal),
            actual.Select(value => $"{value.ClientName}|{value.Method}|{value.PathAndQuery}").Order(StringComparer.Ordinal));
        Assert.All(actual, value => Assert.Equal(ServiceAuthorization, value.Authorization));
        Assert.All(actual.Where(value => value.Method is { } method && (method == HttpMethod.Get || method == HttpMethod.Delete)),
            value => Assert.Null(value.ContentType));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Binary(byte[] body, string contentType) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body) { Headers = { ContentType = new(contentType) } },
    };

    private sealed class RecordingClientFactory(Func<string, RequestSnapshot, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public ConcurrentQueue<RequestSnapshot> Requests { get; } = new();

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(new RecordingHandler(name, async (clientName, request) =>
            {
                Requests.Enqueue(request);
                return await responder(clientName, request);
            }))
            { BaseAddress = new Uri($"https://{name}.test") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "signed-service-token");
            return client;
        }
    }

    private sealed class RecordingHandler(string clientName, Func<string, RequestSnapshot, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = new RequestSnapshot(
                clientName,
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                body,
                request.Content?.Headers.ContentType?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? Assert.Single(values) : null);
            Requests.Add(snapshot);
            return await responder(clientName, snapshot);
        }
    }

    private sealed record RequestSnapshot(
        string ClientName,
        HttpMethod Method,
        string PathAndQuery,
        string? Authorization,
        string? Body,
        string? ContentType,
        string? IdempotencyKey);
}
