using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Strict bearer-authenticated client for supplier APIs.</summary>
public sealed class LegacyProcurementClient(HttpClient httpClient) : ILegacyProcurementClient
{
    /// <inheritdoc />
    public Task<PaginatedResponse<SupplierResponse>?> GetSuppliersAsync(SupplierSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken) => GetAsync<PaginatedResponse<SupplierResponse>>($"/Suppliers?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", token, cancellationToken);
    /// <inheritdoc />
    public Task<SupplierResponse?> GetSupplierAsync(int id, string token, CancellationToken cancellationToken) => GetAsync<SupplierResponse>($"/Suppliers/{id}", token, cancellationToken);
    /// <inheritdoc />
    public Task<SupplierAddressResponse?> GetSupplierAddressAsync(int id, string token, CancellationToken cancellationToken) => GetAsync<SupplierAddressResponse>($"/suppliers/{id}/addresses", token, cancellationToken);
    /// <inheritdoc />
    public async Task<SupplierResponse> CreateSupplierAsync(UpsertSupplierRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "/Suppliers", token, payload);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupplierResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty supplier response.");
    }
    /// <inheritdoc />
    public async Task<SupplierAddressResponse> CreateSupplierAddressAsync(int id, UpsertSupplierAddressRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, $"/suppliers/{id}/addresses", token, payload);
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SupplierAddressResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty supplier address response.");
    }
    /// <inheritdoc />
    public Task UpdateSupplierAsync(int id, UpsertSupplierRequest payload, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Put, $"/Suppliers/{id}", token, payload, cancellationToken);
    /// <inheritdoc />
    public Task UpdateSupplierAddressAsync(int id, UpsertSupplierAddressRequest payload, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Put, $"/suppliers/addresses/{id}", token, payload, cancellationToken);
    /// <inheritdoc />
    public Task DeleteSupplierAsync(int id, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Delete, $"/Suppliers/{id}", token, null, cancellationToken);
    /// <inheritdoc />
    public Task<PaginatedResponse<PurchaseOrderResponse>?> GetPurchaseOrdersAsync(PurchaseOrderSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken) => GetAsync<PaginatedResponse<PurchaseOrderResponse>>($"/PurchaseOrders?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", token, cancellationToken);
    /// <inheritdoc />
    public Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int id, string token, CancellationToken cancellationToken) => GetAsync<PurchaseOrderResponse>($"/PurchaseOrders/{id}", token, cancellationToken);
    /// <inheritdoc />
    public async Task<PurchaseOrderResponse> CreatePurchaseOrderAsync(UpsertPurchaseOrderRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "/PurchaseOrders", token, payload); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PurchaseOrderResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty purchase-order response.");
    }
    /// <inheritdoc />
    public Task DeletePurchaseOrderAsync(int id, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Delete, $"/PurchaseOrders/{id}", token, null, cancellationToken);
    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderAddressResponse>> GetPurchaseOrderAddressesAsync(string token, CancellationToken cancellationToken) => await GetAsync<List<PurchaseOrderAddressResponse>>("/purchaseorders/addresses", token, cancellationToken) ?? [];
    /// <inheritdoc />
    public Task<PurchaseOrderAddressResponse?> GetPurchaseOrderAddressAsync(int id, string token, CancellationToken cancellationToken) => GetAsync<PurchaseOrderAddressResponse>($"/purchaseorders/addresses/{id}", token, cancellationToken);
    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderItemResponse>> GetOrderItemsAsync(int id, string token, CancellationToken cancellationToken) => await GetAsync<List<OrderItemResponse>>($"/purchaseorders/{id}/orderitems", token, cancellationToken) ?? [];
    /// <inheritdoc />
    public async Task<OrderItemResponse> CreateOrderItemAsync(UpsertOrderItemRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "/purchaseorders/orderitems", token, payload); using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderItemResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty order-item response.");
    }
    /// <inheritdoc />
    public Task DeleteOrderItemAsync(int id, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Delete, $"/purchaseorders/orderitems/{id}", token, null, cancellationToken);
    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseOrderFileResponse>> GetPurchaseOrderFilesAsync(int id, string token, CancellationToken cancellationToken) => await GetAsync<List<PurchaseOrderFileResponse>>($"/purchaseorders/{id}/files", token, cancellationToken) ?? [];
    /// <inheritdoc />
    public async Task<PurchaseOrderFileResponse> CreatePurchaseOrderFileAsync(int id, string bucket, string objectName, string token, CancellationToken cancellationToken)
    {
        var uri = $"/purchaseorders/{id}/files?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}";
        using var request = Create(HttpMethod.Post, uri, token, null); using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PurchaseOrderFileResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty purchase-order file response.");
    }
    /// <inheritdoc />
    public Task DeletePurchaseOrderFileAsync(int id, string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Delete, $"/purchaseorders/files/{id}", token, null, cancellationToken);

    private async Task<T?> GetAsync<T>(string uri, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Get, uri, token, null); using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default; response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
    private async Task SendAsync(HttpMethod method, string uri, string token, object? payload, CancellationToken cancellationToken)
    {
        using var request = Create(method, uri, token, payload); using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
    private static HttpRequestMessage Create(HttpMethod method, string uri, string token, object? payload)
    {
        var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null) request.Content = JsonContent.Create(payload); return request;
    }
}