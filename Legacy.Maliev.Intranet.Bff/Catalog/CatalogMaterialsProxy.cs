using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Catalog;

/// <summary>Forwards the browser-safe material list request to CatalogService without business logic.</summary>
public sealed class CatalogMaterialsProxy(HttpClient httpClient)
{
    /// <summary>Gets browser-safe material group lookup values.</summary>
    public Task<HttpResponseMessage> GetMaterialGroupsAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/materials/MaterialGroups", null, cancellationToken);

    /// <summary>Gets browser-safe currency lookup values.</summary>
    public Task<HttpResponseMessage> GetCurrenciesAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/Currencies", null, cancellationToken);

    /// <summary>Creates a complete material while the service credential remains server-side.</summary>
    public Task<HttpResponseMessage> CreateAsync(
        CatalogMaterialUpsertRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "/Materials", request, cancellationToken);

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

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(content);
        }

        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
