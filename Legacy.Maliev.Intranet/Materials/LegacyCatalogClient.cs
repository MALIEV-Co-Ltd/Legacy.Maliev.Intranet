using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Materials;

/// <summary>Strict bearer-authenticated JSON client for CatalogService.</summary>
public sealed class LegacyCatalogClient(HttpClient httpClient) : ILegacyCatalogClient
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<CountryResponse>("/Countries", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<PaginatedMaterialResponse?> GetMaterialsAsync(MaterialSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) =>
        GetOptionalAsync<PaginatedMaterialResponse>($"/Materials?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<MaterialResponse?> GetMaterialAsync(int id, string accessToken, CancellationToken cancellationToken) =>
        GetOptionalAsync<MaterialResponse>($"/Materials/{id}", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<MaterialGroupResponse>("/materials/MaterialGroups", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<CurrencyResponse>("/Currencies", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<ColorResponse>> GetColorsAsync(string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<ColorResponse>("/materials/Colors", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<SurfaceFinishResponse>("/materials/SurfaceFinishes", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<ColorResponse>> GetMaterialColorsAsync(int id, string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<ColorResponse>($"/materials/{id}/colors", accessToken, cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyList<SurfaceFinishResponse>> GetMaterialSurfaceFinishesAsync(int id, string accessToken, CancellationToken cancellationToken) =>
        GetListAsync<SurfaceFinishResponse>($"/materials/{id}/surfacefinishes", accessToken, cancellationToken);

    /// <inheritdoc />
    public async Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest payload, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "/Materials", accessToken, payload);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MaterialResponse>(cancellationToken)
            ?? throw new InvalidOperationException("CatalogService returned an empty material response.");
    }

    /// <inheritdoc />
    public async Task UpdateMaterialAsync(int id, UpsertMaterialRequest payload, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, $"/Materials/{id}", accessToken, payload);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task SyncMaterialColorsAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken)
    {
        var current = await GetMaterialColorsAsync(id, accessToken, cancellationToken);
        await SyncLinksAsync(id, "colors", current.Select(x => x.Id), selectedIds, accessToken, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SyncMaterialSurfaceFinishesAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken)
    {
        var current = await GetMaterialSurfaceFinishesAsync(id, accessToken, cancellationToken);
        await SyncLinksAsync(id, "surfacefinishes", current.Select(x => x.Id), selectedIds, accessToken, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(string uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, uri, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, string accessToken, CancellationToken cancellationToken)
    {
        var result = await GetOptionalAsync<IReadOnlyList<T>>(uri, accessToken, cancellationToken);
        return result ?? [];
    }

    private async Task SyncLinksAsync(int materialId, string segment, IEnumerable<int> currentIds, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken)
    {
        var current = currentIds.ToHashSet();
        var selected = selectedIds.ToHashSet();
        foreach (var removed in current.Except(selected))
        {
            await SendNoContentAsync(HttpMethod.Delete, $"/materials/{materialId}/{segment}/{removed}", accessToken, cancellationToken);
        }
        foreach (var added in selected.Except(current))
        {
            await SendNoContentAsync(HttpMethod.Post, $"/materials/{materialId}/{segment}/{added}", accessToken, cancellationToken);
        }
    }

    private async Task SendNoContentAsync(HttpMethod method, string uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, uri, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken, object? payload = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        return request;
    }
}