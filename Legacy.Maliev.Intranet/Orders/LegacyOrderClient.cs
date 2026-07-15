using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

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

    private async Task<T?> GetAsync<T>(string uri, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
