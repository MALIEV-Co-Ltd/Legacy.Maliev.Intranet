using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Forwards ProcurementService supplier reads without exposing service credentials to the browser.</summary>
public sealed class SuppliersProxy(HttpClient httpClient)
{
    /// <summary>Gets the requested supplier page.</summary>
    public async Task<HttpResponseMessage> GetAsync(
        SupplierListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/Suppliers?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
