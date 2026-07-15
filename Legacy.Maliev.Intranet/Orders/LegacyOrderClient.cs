using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;

namespace Legacy.Maliev.Intranet.Orders;

/// <summary>Strict bearer-authenticated client for OrderService reads.</summary>
public sealed class LegacyOrderClient(HttpClient httpClient) : ILegacyOrderClient
{
    /// <inheritdoc />
    public Task<PaginatedResponse<OrderResponse>?> GetOrdersAsync(
        OrderSortType sort,
        string? search,
        int index,
        int size,
        string token,
        CancellationToken cancellationToken) =>
        GetAsync<PaginatedResponse<OrderResponse>>(
            $"/Orders?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}",
            token,
            cancellationToken);

    /// <inheritdoc />
    public Task<PaginatedResponse<OrderResponse>?> GetPendingOrdersAsync(
        int size,
        string token,
        CancellationToken cancellationToken) =>
        GetAsync<PaginatedResponse<OrderResponse>>($"/Orders/pending?index=1&size={size}", token, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessResponse>> GetProcessesAsync(string token, CancellationToken cancellationToken) =>
        await GetAsync<List<ProcessResponse>>("/Processes", token, cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<OrderResponse?> GetOrderAsync(int id, string token, CancellationToken cancellationToken) =>
        GetAsync<OrderResponse>($"/Orders/{id}", token, cancellationToken);

    /// <inheritdoc />
    public async Task<OrderResponse> CreateOrderAsync(UpsertOrderRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "/Orders", token, payload);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty order response.");
    }

    /// <inheritdoc />
    public async Task UpdateOrderAsync(int id, UpsertOrderRequest payload, DateTimeOffset? expectedModifiedDate, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Put, $"/Orders/{id}", token, payload);
        if (expectedModifiedDate is not null)
            request.Headers.Add("X-Expected-Modified-Date", expectedModifiedDate.Value.ToString("O", CultureInfo.InvariantCulture));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public Task DeleteOrderAsync(int id, string token, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/Orders/{id}", token, null, false, cancellationToken);

    /// <inheritdoc />
    public async Task CreateNewOrderStatusAsync(int orderId, string token, CancellationToken cancellationToken)
    {
        var status = await GetAsync<OrderStatusResponse>("/OrderStatuses/New", token, cancellationToken)
            ?? throw new InvalidOperationException("The required New order status is unavailable.");
        await TransitionOrderAsync(orderId, status.Id, token, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OrderStatusResponse?> GetLatestStatusAsync(int orderId, string token, CancellationToken cancellationToken) =>
        GetAsync<OrderStatusResponse>($"/orderstatuses/Histories/{orderId}/latest", token, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderStatusHistoryResponse>> GetStatusHistoryAsync(int orderId, string token, CancellationToken cancellationToken) =>
        await GetAsync<List<OrderStatusHistoryResponse>>($"/orderstatuses/Histories/{orderId}", token, cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderStatusResponse>> GetAvailableStatusesAsync(int currentStatusId, string token, CancellationToken cancellationToken) =>
        await GetAsync<List<OrderStatusResponse>>($"/orderstatuses/{currentStatusId}/available", token, cancellationToken) ?? [];

    /// <inheritdoc />
    public Task TransitionOrderAsync(int orderId, int statusId, string token, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/orderstatuses/Histories/{orderId}/{statusId}", token, null, true, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderFileResponse>> GetOrderFilesAsync(int orderId, string token, CancellationToken cancellationToken) =>
        await GetAsync<List<OrderFileResponse>>($"/orders/{orderId}/files", token, cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<OrderFileResponse> CreateOrderFileAsync(int orderId, string bucket, string objectName, string token, CancellationToken cancellationToken)
    {
        var uri = $"/orders/{orderId}/files?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}";
        using var request = Create(HttpMethod.Post, uri, token, null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderFileResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty order-file response.");
    }

    /// <inheritdoc />
    public Task DeleteOrderFileAsync(int fileId, string token, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/orders/files/{fileId}", token, null, false, cancellationToken);

    private async Task<T?> GetAsync<T>(string uri, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string uri, string token, object? payload, bool idempotent, CancellationToken cancellationToken)
    {
        using var request = Create(method, uri, token, payload);
        if (idempotent) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage Create(HttpMethod method, string uri, string token, object? payload)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return request;
    }
}
