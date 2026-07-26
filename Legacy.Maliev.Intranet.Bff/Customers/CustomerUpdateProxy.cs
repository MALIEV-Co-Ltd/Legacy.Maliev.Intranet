using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Forwards one customer profile replacement through a non-retrying write client.</summary>
public sealed class CustomerUpdateProxy(HttpClient httpClient)
{
    /// <summary>
    /// Replaces only the editable profile fields while preserving existing company and address links.
    /// </summary>
    public Task<HttpResponseMessage> UpdateAsync(
        int id,
        CustomerUpdateRequest input,
        CustomerDetail current,
        CancellationToken cancellationToken)
    {
        var payload = new UpsertCustomerPayload(
            input.FirstName,
            input.LastName,
            input.Telephone,
            input.Mobile,
            input.Fax,
            input.Email,
            input.DateOfBirth,
            current.CompanyId,
            current.BillingAddressId,
            current.ShippingAddressId);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/customers/{id}")
        {
            Content = JsonContent.Create(payload),
        };
        return SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }

    private sealed record UpsertCustomerPayload(
        string FirstName,
        string LastName,
        string? Telephone,
        string? Mobile,
        string? Fax,
        string Email,
        DateTime? DateOfBirth,
        int? CompanyId,
        int? BillingAddressId,
        int? ShippingAddressId);
}
