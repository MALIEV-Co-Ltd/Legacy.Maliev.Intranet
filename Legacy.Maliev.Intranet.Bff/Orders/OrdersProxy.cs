using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Forwards OrderService reads without exposing service credentials to the browser.</summary>
public sealed class OrdersProxy(HttpClient httpClient)
{
    /// <summary>Gets the requested order page.</summary>
    public async Task<HttpResponseMessage> GetAsync(
        OrderListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/Orders?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Gets the bounded pending order working set.</summary>
    public async Task<HttpResponseMessage> GetPendingAsync(int size, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Orders/pending?index=1&size={size}");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Gets OrderService process labels from the actual controller route.</summary>
    public async Task<HttpResponseMessage> GetProcessesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/processes");
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
