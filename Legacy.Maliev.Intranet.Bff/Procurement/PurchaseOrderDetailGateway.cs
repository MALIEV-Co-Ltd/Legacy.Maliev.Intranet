using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.PurchaseOrders;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Transport-only adapter for purchase-order detail, signed download, and deletion operations.</summary>
public sealed class PurchaseOrderDetailGateway(IHttpClientFactory clients) : IPurchaseOrderDetailGateway
{
    /// <inheritdoc />
    public Task<PurchaseOrderDetailData?> GetOrderAsync(int id, CancellationToken cancellationToken) =>
        GetOptionalAsync<PurchaseOrderDetailData>(PurchaseOrderCreationGateway.ProcurementClient, $"/PurchaseOrders/{id}", cancellationToken);
    /// <inheritdoc />
    public async Task<string?> GetSupplierNameAsync(int id, CancellationToken cancellationToken) =>
        (await GetOptionalAsync<NameData>(PurchaseOrderCreationGateway.ProcurementClient, $"/Suppliers/{id}", cancellationToken))?.Name;
    /// <inheritdoc />
    public async Task<string?> GetEmployeeNameAsync(int id, CancellationToken cancellationToken) =>
        (await GetOptionalAsync<EmployeeNameData>(PurchaseOrderCreationGateway.EmployeeClient, $"/employees/{id}", cancellationToken))?.FullName;
    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderDetailItemData>> GetItemsAsync(int orderId, CancellationToken cancellationToken) =>
        await GetOptionalAsync<IReadOnlyList<PurchaseOrderDetailItemData>>(PurchaseOrderCreationGateway.ProcurementClient, $"/purchaseorders/{orderId}/orderitems", cancellationToken) ?? [];
    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderDetailFileData>> GetFilesAsync(int orderId, CancellationToken cancellationToken) =>
        await GetOptionalAsync<IReadOnlyList<PurchaseOrderDetailFileData>>(PurchaseOrderCreationGateway.ProcurementClient, $"/purchaseorders/{orderId}/files", cancellationToken) ?? [];
    /// <inheritdoc />
    public Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        GetOptionalAsync<Uri>(PurchaseOrderCreationGateway.FileClient, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteStoredFileAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        DeleteAsync(PurchaseOrderCreationGateway.FileClient, $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteFileLinkAsync(int id, CancellationToken cancellationToken) =>
        DeleteAsync(PurchaseOrderCreationGateway.ProcurementClient, $"/purchaseorders/files/{id}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteItemAsync(int id, CancellationToken cancellationToken) =>
        DeleteAsync(PurchaseOrderCreationGateway.ProcurementClient, $"/purchaseorders/orderitems/{id}", cancellationToken);
    /// <inheritdoc />
    public Task DeleteOrderAsync(int id, CancellationToken cancellationToken) =>
        DeleteAsync(PurchaseOrderCreationGateway.ProcurementClient, $"/PurchaseOrders/{id}", cancellationToken);

    private async Task<T?> GetOptionalAsync<T>(string clientName, string uri, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(clientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        EnsureSuccess(response);
        try { return await response.Content.ReadFromJsonAsync<T>(cancellationToken); }
        catch (JsonException exception) { throw new PurchaseOrderDetailGatewayException(PurchaseOrderDetailStatus.BadGateway, innerException: exception); }
    }

    private async Task DeleteAsync(string clientName, string uri, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(clientName).DeleteAsync(uri, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) EnsureSuccess(response);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var status = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => PurchaseOrderDetailStatus.BadRequest,
            HttpStatusCode.Unauthorized => PurchaseOrderDetailStatus.Unauthorized,
            HttpStatusCode.Forbidden => PurchaseOrderDetailStatus.Forbidden,
            HttpStatusCode.TooManyRequests => PurchaseOrderDetailStatus.RateLimited,
            _ when (int)response.StatusCode >= 500 => PurchaseOrderDetailStatus.Unavailable,
            _ => PurchaseOrderDetailStatus.BadGateway,
        };
        throw new PurchaseOrderDetailGatewayException(status, response.Headers.RetryAfter?.Delta);
    }

    private sealed record NameData(string? Name);
    private sealed record EmployeeNameData(string? FullName);
}
