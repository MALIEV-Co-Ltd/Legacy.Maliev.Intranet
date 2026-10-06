using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Dedicated Customer profile transport sharing genuine employee admission with the identity client.</summary>
/// <remarks>Registration must remove inherited retry handlers and disable redirects; callers own responses.</remarks>
public sealed class CustomerAdministrationProfileClient(HttpClient client, CustomerAdministrationIdentityClient credentials)
{
    /// <summary>Reads the uncached profile and its Customer xmin ETag.</summary>
    public Task<HttpResponseMessage> ReadAsync(int customerId, HttpContext context, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        return credentials.SendAsync(client, new(HttpMethod.Get, $"/customers/{customerId}/versioned"),
            "legacy-customer.customers.read", context, token);
    }

    /// <summary>Writes only profile scalars while preserving preflight-derived relations and the original captured version.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int customerId, CustomerUpdateRequest input,
        CustomerDetail current, string version, HttpContext context, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);
        if (current.Id != customerId || !CustomerAdministrationVersion.IsProfile(version))
            throw new ArgumentException("A bound profile and captured Customer version are required.", nameof(current));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/customers/{customerId}/versioned")
        {
            Content = JsonContent.Create(new ProfilePayload(input.FirstName, input.LastName, input.Telephone,
                input.Mobile, input.Fax, input.Email, input.DateOfBirth,
                current.CompanyId, current.BillingAddressId, current.ShippingAddressId)),
        };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(version));
        return credentials.SendAsync(client, request, "legacy-customer.customers.update", context, token);
    }

    private sealed record ProfilePayload(string FirstName, string LastName, string? Telephone,
        string? Mobile, string? Fax, string Email, DateTime? DateOfBirth,
        int? CompanyId, int? BillingAddressId, int? ShippingAddressId);
}
