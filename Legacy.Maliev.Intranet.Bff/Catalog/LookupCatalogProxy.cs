using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Catalog;

/// <summary>Bounded Catalog requests using the BFF's existing server-held authentication.</summary>
public sealed class LookupCatalogProxy(HttpClient http) : IDisposable
{
    /// <summary>Gets one bounded lookup resource with allowlisted query values.</summary>
    public async Task<HttpResponseMessage> GetAsync(string resource, IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var path = "/api/v1/" + resource + "?" + string.Join("&", query
            .Where(pair => pair.Value is not null)
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value!)));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    /// <summary>Forwards reviewed pasted text without logging the request body.</summary>
    public async Task<HttpResponseMessage> ResolveAsync(LookupResolveRequest input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/thai-addresses/resolve")
        { Content = JsonContent.Create(input) };
        return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();
}
