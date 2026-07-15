using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Employees;

/// <summary>Strict bearer-authenticated JSON client for EmployeeService.</summary>
public sealed class LegacyEmployeeClient(HttpClient httpClient) : ILegacyEmployeeClient
{
    /// <inheritdoc />
    public async Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken)
    {
        var query = $"/employees?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = CreateRequest(HttpMethod.Get, query, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaginatedResponse<EmployeeResponse>>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeResponse?> GetEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/employees/{id}", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeResponse>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest profile, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "/employees", accessToken);
        request.Content = JsonContent.Create(profile);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeResponse>(cancellationToken)
            ?? throw new InvalidOperationException("EmployeeService returned an empty create response.");
    }

    /// <inheritdoc />
    public async Task DeleteEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/employees/{id}", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
