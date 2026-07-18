using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Suppliers;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Forwards supplier management calls using the server-owned service identity.</summary>
public sealed class SupplierManagementClient(HttpClient httpClient) : ISupplierManagementClient
{
    /// <inheritdoc />
    public Task<HttpResponseMessage> GetProfileAsync(int id, CancellationToken ct) => SendAsync(new(HttpMethod.Get, $"/Suppliers/{id}"), ct);
    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAddressAsync(int id, CancellationToken ct) => SendAsync(new(HttpMethod.Get, $"/suppliers/{id}/addresses"), ct);
    /// <inheritdoc />
    public Task<HttpResponseMessage> UpdateProfileAsync(int id, SupplierCreateRequest request, CancellationToken ct) => SendAsync(Json(HttpMethod.Put, $"/Suppliers/{id}", new Profile(request.Name, request.Website, request.TaxNumber, request.Email, request.Note, request.Telephone, request.Mobile, request.Fax)), ct);
    /// <inheritdoc />
    public Task<HttpResponseMessage> CreateAddressAsync(int id, SupplierCreateRequest request, CancellationToken ct) => SendAsync(Json(HttpMethod.Post, $"/suppliers/{id}/addresses", Address(request)), ct);
    /// <inheritdoc />
    public Task<HttpResponseMessage> UpdateAddressAsync(int id, SupplierCreateRequest request, CancellationToken ct) => SendAsync(Json(HttpMethod.Put, $"/suppliers/addresses/{id}", Address(request)), ct);
    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteProfileAsync(int id, CancellationToken ct) => SendAsync(new(HttpMethod.Delete, $"/Suppliers/{id}"), ct);
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { using (request) return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
    private static HttpRequestMessage Json(HttpMethod method, string path, object body) => new(method, path) { Content = JsonContent.Create(body) };
    private static AddressPayload Address(SupplierCreateRequest value) => new(value.Building, value.Address1, value.Address2, value.City, value.State, value.PostalCode, value.CountryId);
    private sealed record Profile(string Name, string? Website, string? TaxNumber, string? Email, string? Note, string? Telephone, string? Mobile, string? Fax);
    private sealed record AddressPayload(string? Building, string Address1, string? Address2, string? City, string? State, string? PostalCode, int CountryId);
}
