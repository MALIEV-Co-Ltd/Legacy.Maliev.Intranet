using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Suppliers;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

/// <summary>Forwards supplier profile writes using the server-owned service identity.</summary>
public sealed class SupplierProfileCreationClient(HttpClient httpClient) : ISupplierProfileCreationClient
{
    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken)
    {
        var payload = new ProfilePayload(request.Name, request.Website, request.TaxNumber, request.Email, request.Note, request.Telephone, request.Mobile, request.Fax);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/Suppliers") { Content = JsonContent.Create(payload) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> DeleteAsync(int supplierId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"/Suppliers/{supplierId}");
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record ProfilePayload(string Name, string? Website, string? TaxNumber, string? Email, string? Note, string? Telephone, string? Mobile, string? Fax);
}

/// <summary>Forwards supplier-owned address writes using the server-owned service identity.</summary>
public sealed class SupplierAddressCreationClient(HttpClient httpClient) : ISupplierAddressCreationClient
{
    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(int supplierId, SupplierCreateRequest request, CancellationToken cancellationToken)
    {
        var payload = new AddressPayload(request.Building, request.Address1, request.Address2, request.City, request.State, request.PostalCode, request.CountryId);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/suppliers/{supplierId}/addresses") { Content = JsonContent.Create(payload) };
        return await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private sealed record AddressPayload(string? Building, string Address1, string? Address2, string? City, string? State, string? PostalCode, int CountryId);
}
