using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Customers;

/// <summary>Strict bearer-authenticated JSON client for CustomerService.</summary>
public sealed class LegacyCustomerClient(HttpClient httpClient) : ILegacyCustomerClient
{
    /// <inheritdoc />
    public async Task<PaginatedResponse<CustomerResponse>?> GetCustomersAsync(
        CustomerSortType sort,
        string? search,
        int index,
        int size,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var query = $"/customers?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = CreateRequest(HttpMethod.Get, query, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaginatedResponse<CustomerResponse>>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerResponse?> GetCustomerAsync(int id, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/customers/{id}", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerResponse>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerResponse> CreateCustomerAsync(
        UpsertCustomerRequest profile,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "/customers", accessToken);
        request.Content = JsonContent.Create(profile);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerResponse>(cancellationToken)
            ?? throw new InvalidOperationException("CustomerService returned an empty create response.");
    }

    /// <inheritdoc />
    public async Task DeleteCustomerAsync(int id, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/customers/{id}", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}