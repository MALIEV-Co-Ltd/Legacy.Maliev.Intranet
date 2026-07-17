using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Catalog;

/// <summary>Forwards the browser-safe material list request to CatalogService without business logic.</summary>
public sealed class CatalogMaterialsProxy(HttpClient httpClient)
{
    /// <summary>Gets one read-only material projection with the service token kept server-side.</summary>
    public Task<HttpResponseMessage> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/Materials/{id}");
        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Gets the requested material page with the employee token kept server-side.</summary>
    public Task<HttpResponseMessage> GetAsync(
        CatalogMaterialSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/Materials?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
