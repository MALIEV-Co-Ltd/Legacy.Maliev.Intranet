using Legacy.Maliev.Intranet.Contracts;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Customers;

/// <summary>Strict server-authenticated CustomerService profile client.</summary>
public sealed class CustomerProfileCreationClient(HttpClient httpClient) : ICustomerProfileCreationClient
{
    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(
        CreateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/customers")
        {
            Content = JsonContent.Create(new CustomerProfileRequest(
                request.FirstName,
                request.LastName,
                request.Telephone,
                request.Mobile,
                request.Fax,
                request.Email,
                request.DateOfBirth,
                null,
                null,
                null)),
        };
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> DeleteAsync(int customerId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"/customers/{customerId}");
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record CustomerProfileRequest(
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

/// <summary>Strict server-authenticated AuthService customer-identity client.</summary>
public sealed class CustomerIdentityCreationClient(HttpClient httpClient) : ICustomerIdentityCreationClient
{
    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(
        int customerId,
        CreateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/auth/v1/customer-identities/{customerId}")
        {
            Content = JsonContent.Create(new CustomerIdentityRequest(
                request.Email,
                request.Email,
                request.Password,
                true,
                request.Telephone,
                request.Fax,
                request.Mobile)),
        };
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record CustomerIdentityRequest(
        string UserName,
        string Email,
        string Password,
        bool EmailConfirmed,
        string? PhoneNumber,
        string? FaxNumber,
        string? MobileNumber);
}
