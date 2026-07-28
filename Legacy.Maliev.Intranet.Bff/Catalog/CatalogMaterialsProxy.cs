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

    /// <summary>Gets the complete legacy color lookup used by material editing.</summary>
    public Task<HttpResponseMessage> GetColorsAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/materials/Colors", null, cancellationToken);

    /// <summary>Gets colors currently linked to a material.</summary>
    public Task<HttpResponseMessage> GetMaterialColorsAsync(int materialId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/materials/{materialId}/colors", null, cancellationToken);

    /// <summary>Adds one material/color relationship through CatalogService.</summary>
    public Task<HttpResponseMessage> AddMaterialColorAsync(
        int materialId,
        int colorId,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/materials/{materialId}/colors/{colorId}", null, cancellationToken);

    /// <summary>Removes one material/color relationship through CatalogService.</summary>
    public Task<HttpResponseMessage> RemoveMaterialColorAsync(
        int materialId,
        int colorId,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/materials/{materialId}/colors/{colorId}", null, cancellationToken);

    /// <summary>Gets the complete legacy surface-finish lookup used by material editing.</summary>
    public Task<HttpResponseMessage> GetSurfaceFinishesAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/materials/SurfaceFinishes", null, cancellationToken);

    /// <summary>Gets surface finishes currently linked to a material.</summary>
    public Task<HttpResponseMessage> GetMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/materials/{materialId}/surfacefinishes", null, cancellationToken);

    /// <summary>Adds one material/surface-finish relationship through CatalogService.</summary>
    public Task<HttpResponseMessage> AddMaterialSurfaceFinishAsync(
        int materialId,
        int surfaceFinishId,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"/materials/{materialId}/surfacefinishes/{surfaceFinishId}", null, cancellationToken);

    /// <summary>Removes one material/surface-finish relationship through CatalogService.</summary>
    public Task<HttpResponseMessage> RemoveMaterialSurfaceFinishAsync(
        int materialId,
        int surfaceFinishId,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/materials/{materialId}/surfacefinishes/{surfaceFinishId}", null, cancellationToken);

    /// <summary>Creates a complete material while the service credential remains server-side.</summary>
    public Task<HttpResponseMessage> CreateAsync(
        CatalogMaterialUpsertRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "/Materials", request, cancellationToken);

    /// <summary>Updates a complete material while the service credential remains server-side.</summary>
    public Task<HttpResponseMessage> UpdateAsync(
        int id,
        CatalogMaterialUpsertRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"/Materials/{id}", request, cancellationToken);

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
