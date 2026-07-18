using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.PurchaseOrders;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Transport-only adapter for the independently deployed purchase-order services.</summary>
public sealed class PurchaseOrderCreationGateway(IHttpClientFactory clients) : IPurchaseOrderCreationGateway
{
    /// <summary>Named ProcurementService client.</summary>
    public const string ProcurementClient = "purchase-order-procurement";
    /// <summary>Named EmployeeService client.</summary>
    public const string EmployeeClient = "purchase-order-employee";
    /// <summary>Named CatalogService client.</summary>
    public const string CatalogClient = "purchase-order-catalog";
    /// <summary>Named DocumentService client.</summary>
    public const string DocumentClient = "purchase-order-document";
    /// <summary>Named FileService client.</summary>
    public const string FileClient = "purchase-order-file";

    /// <inheritdoc />
    public async Task<PurchaseOrderCreateOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var procurement = clients.CreateClient(ProcurementClient);
        var employee = clients.CreateClient(EmployeeClient);
        var suppliersTask = GetAsync<SupplierPage>(procurement, "/Suppliers?sort=SupplierName_Ascending&search=&index=1&size=250", cancellationToken);
        var employeesTask = GetAsync<EmployeePage>(employee, "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250", cancellationToken);
        var addressesTask = GetAsync<IReadOnlyList<AddressData>>(procurement, "/purchaseorders/addresses", cancellationToken);
        await Task.WhenAll(suppliersTask, employeesTask, addressesTask);
        return new(
            suppliersTask.Result.Items.Where(value => value.Id > 0 && !string.IsNullOrWhiteSpace(value.Name)).Select(value => new PurchaseOrderSupplierOption(value.Id, value.Name!)).ToArray(),
            employeesTask.Result.Items.Where(value => value.Id > 0 && !string.IsNullOrWhiteSpace(value.FullName)).Select(value => new PurchaseOrderEmployeeOption(value.Id, value.FullName)).ToArray(),
            addressesTask.Result.Where(value => value.Id > 0 && !string.IsNullOrWhiteSpace(value.AddressLine1)).Select(value => new PurchaseOrderAddressOption(value.Id, value.AddressLine1, value.City)).ToArray());
    }

    /// <inheritdoc />
    public async Task<PurchaseOrderCreatedData> CreateOrderAsync(PurchaseOrderCreateRequest request, string attemptId, CancellationToken cancellationToken)
    {
        var payload = new OrderWrite(request.SupplierId, request.SupplierContactPerson, request.ShippingAddressId,
            request.ShippingContactPerson, request.ShippingTelephone, request.ShippingMobile, request.ShippingFax,
            request.BillingAddressId, request.BillingContactPerson, request.BillingTelephone, request.BillingMobile,
            request.BillingFax, request.Fob, request.Terms, request.ShippingMethod, request.EmployeeId, request.Notes);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/PurchaseOrders") { Content = JsonContent.Create(payload) };
        message.Headers.Add("Idempotency-Key", attemptId);
        return MapOrder(await SendAsync<OrderData>(clients.CreateClient(ProcurementClient), message, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<int> CreateItemAsync(int purchaseOrderId, PurchaseOrderCreateItem item, string attemptId, int itemIndex, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/purchaseorders/orderitems")
        {
            Content = JsonContent.Create(new ItemWrite(purchaseOrderId, item.PartNumber, item.Description, item.Quantity, item.UnitPrice)),
        };
        message.Headers.Add("Idempotency-Key", OperationId(attemptId, $"item:{itemIndex}"));
        return (await SendAsync<CreatedId>(clients.CreateClient(ProcurementClient), message, cancellationToken)).Id;
    }

    /// <inheritdoc />
    public async Task<PurchaseOrderDocumentReferences> GetDocumentReferencesAsync(PurchaseOrderCreateRequest request, CancellationToken cancellationToken)
    {
        var procurement = clients.CreateClient(ProcurementClient);
        var supplierTask = GetAsync<SupplierData>(procurement, $"/Suppliers/{request.SupplierId}", cancellationToken);
        var supplierAddressTask = GetAsync<SupplierAddressData>(procurement, $"/suppliers/{request.SupplierId}/addresses", cancellationToken);
        var shippingTask = GetAsync<AddressData>(procurement, $"/purchaseorders/addresses/{request.ShippingAddressId}", cancellationToken);
        var billingTask = GetAsync<AddressData>(procurement, $"/purchaseorders/addresses/{request.BillingAddressId}", cancellationToken);
        var employeeTask = GetAsync<EmployeeData>(clients.CreateClient(EmployeeClient), $"/employees/{request.EmployeeId}", cancellationToken);
        var countriesTask = GetAsync<IReadOnlyList<CountryData>>(clients.CreateClient(CatalogClient), "/Countries", cancellationToken);
        await Task.WhenAll(supplierTask, supplierAddressTask, shippingTask, billingTask, employeeTask, countriesTask);
        var supplier = supplierTask.Result;
        var supplierAddress = supplierAddressTask.Result;
        return new(
            new(supplier.Name ?? string.Empty, request.SupplierContactPerson, supplier.Telephone, supplier.Mobile, supplier.Fax, Address(supplierAddress)),
            Address(shippingTask.Result), Address(billingTask.Result), employeeTask.Result.FullName,
            countriesTask.Result.Where(value => value.Id > 0).ToDictionary(value => value.Id, value => value.Name));
    }

    /// <inheritdoc />
    public async Task<byte[]> RenderPdfAsync(PurchaseOrderPdfDocument document, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/Pdfs/purchaseorder") { Content = JsonContent.Create(document) };
        using var response = await clients.CreateClient(DocumentClient).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType is not "application/pdf")
        {
            throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
        }
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PurchaseOrderStoredFile> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string attemptId, CancellationToken cancellationToken)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "files", $"PurchaseOrder_{purchaseOrderId}.pdf");
        var path = $"purchaseorders/{purchaseOrderId}";
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(path)}") { Content = multipart };
        message.Headers.Add("Idempotency-Key", OperationId(attemptId, "pdf-upload"));
        var result = await SendAsync<UploadResult>(clients.CreateClient(FileClient), message, cancellationToken);
        var stored = result.Object.SingleOrDefault() ?? throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
        return new(stored.Bucket, stored.ObjectName);
    }

    /// <inheritdoc />
    public async Task<int> LinkFileAsync(int purchaseOrderId, PurchaseOrderStoredFile file, string attemptId, CancellationToken cancellationToken)
    {
        var uri = $"/purchaseorders/{purchaseOrderId}/files?bucket={Uri.EscapeDataString(file.Bucket)}&objectName={Uri.EscapeDataString(file.ObjectName)}";
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Add("Idempotency-Key", OperationId(attemptId, "file-link"));
        return (await SendAsync<CreatedId>(clients.CreateClient(ProcurementClient), message, cancellationToken)).Id;
    }

    /// <inheritdoc />
    public Task DeleteFileLinkAsync(int fileId, CancellationToken cancellationToken) => DeleteAsync(ProcurementClient, $"/purchaseorders/files/{fileId}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteStoredFileAsync(PurchaseOrderStoredFile file, CancellationToken cancellationToken) => DeleteAsync(FileClient, $"/Uploads?bucket={Uri.EscapeDataString(file.Bucket)}&objectName={Uri.EscapeDataString(file.ObjectName)}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteItemAsync(int itemId, CancellationToken cancellationToken) => DeleteAsync(ProcurementClient, $"/purchaseorders/orderitems/{itemId}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteOrderAsync(int purchaseOrderId, CancellationToken cancellationToken) => DeleteAsync(ProcurementClient, $"/PurchaseOrders/{purchaseOrderId}", cancellationToken);

    private async Task DeleteAsync(string clientName, string uri, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(clientName).DeleteAsync(uri, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) EnsureSuccess(response);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync<T>(client, message, cancellationToken);
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
        }
        catch (JsonException exception)
        {
            throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway, innerException: exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => PurchaseOrderCreationStatus.BadRequest,
            HttpStatusCode.Unauthorized => PurchaseOrderCreationStatus.Unauthorized,
            HttpStatusCode.Forbidden => PurchaseOrderCreationStatus.Forbidden,
            HttpStatusCode.Conflict => PurchaseOrderCreationStatus.Conflict,
            HttpStatusCode.TooManyRequests => PurchaseOrderCreationStatus.RateLimited,
            HttpStatusCode.NotFound => PurchaseOrderCreationStatus.BadGateway,
            _ when (int)response.StatusCode >= 500 => PurchaseOrderCreationStatus.Unavailable,
            _ => PurchaseOrderCreationStatus.BadGateway,
        };
        var retryAfter = response.Headers.RetryAfter?.Delta;
        throw new PurchaseOrderGatewayException(status, retryAfter);
    }

    private static PurchaseOrderCreatedData MapOrder(OrderData value) =>
        value.Id > 0 && value.CreatedDate is { } date ? new(value.Id, date) : throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);

    private static string OperationId(string attemptId, string operation)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{attemptId}:{operation}"));
        Span<byte> value = stackalloc byte[16];
        hash.AsSpan(0, value.Length).CopyTo(value);
        value[7] = (byte)((value[7] & 0x0f) | 0x50);
        value[8] = (byte)((value[8] & 0x3f) | 0x80);
        return new Guid(value).ToString("D");
    }

    private static PurchaseOrderPostalAddress Address(AddressData value) => new(value.AddressLine1, value.AddressLine2, value.Building, value.City, value.State, value.PostalCode, value.CountryId);
    private static PurchaseOrderPostalAddress Address(SupplierAddressData value) => new(value.Address1, value.Address2, value.Building, value.City, value.State, value.PostalCode, value.CountryId);

    private sealed record SupplierPage(IReadOnlyList<SupplierData> Items);
    private sealed record EmployeePage(IReadOnlyList<EmployeeData> Items);
    private sealed record SupplierData(int Id, string? Name, string? Telephone, string? Mobile, string? Fax);
    private sealed record EmployeeData(int Id, string FullName);
    private sealed record AddressData(int Id, string AddressLine1, string? AddressLine2, string? Building, string? City, string? State, string? PostalCode, int CountryId);
    private sealed record SupplierAddressData(int Id, string? Address1, string? Address2, string? Building, string? City, string? State, string? PostalCode, int CountryId);
    private sealed record CountryData(int Id, string Name);
    private sealed record OrderData(int Id, DateTime? CreatedDate);
    private sealed record CreatedId(int Id);
    private sealed record UploadResult(IReadOnlyList<StoredObject> Object);
    private sealed record StoredObject(string Bucket, string ObjectName);
    private sealed record OrderWrite(int? SupplierId, string? SupplierContactPerson, int? ShippingAddressId, string? ShippingContactPerson, string? ShippingTelephone, string? ShippingMobile, string? ShippingFax, int? BillingAddressId, string? BillingContactPerson, string? BillingTelephone, string? BillingMobile, string? BillingFax, string? Fob, string? Terms, string? ShippingMethod, int? EmployeeId, string? Notes);
    private sealed record ItemWrite(int? PurchaseOrderId, string? PartNumber, string? Description, int? Quantity, decimal? UnitPrice);
}
