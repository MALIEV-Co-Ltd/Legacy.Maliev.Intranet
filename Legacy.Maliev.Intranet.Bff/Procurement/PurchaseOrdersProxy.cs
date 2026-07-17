using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Forwards ProcurementService purchase-order reads without exposing service credentials to the browser.</summary>
public sealed class PurchaseOrdersProxy(HttpClient httpClient)
{
    /// <summary>Gets the requested purchase-order page.</summary>
    public async Task<HttpResponseMessage> GetAsync(
        PurchaseOrderListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/PurchaseOrders?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
