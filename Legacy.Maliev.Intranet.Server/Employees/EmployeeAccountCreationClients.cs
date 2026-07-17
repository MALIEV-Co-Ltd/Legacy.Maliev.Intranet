using Legacy.Maliev.Intranet.Contracts;
using System.Net.Http.Json;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Employees;

/// <summary>
/// Sends employee profile creation and compensation requests to the legacy Employee Service.
/// </summary>
/// <param name="httpClient">The service-authenticated HTTP client configured for the Employee Service.</param>
public sealed class EmployeeProfileCreationClient(HttpClient httpClient) : IEmployeeProfileCreationClient
{
    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(CreateEmployeeAccountRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/employees")
        {
            Content = JsonContent.Create(new EmployeeProfileRequest(
                request.RoleId, request.FirstName, request.LastName, request.PhoneNumber,
                request.Email, request.DateOfBirth, null), options: LegacyJson),
        };
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> DeleteAsync(int employeeId, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"/employees/{employeeId}");
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record EmployeeProfileRequest(int? RoleId, string FirstName, string LastName, string? PhoneNumber, string Email, DateTime? DateOfBirth, int? HomeAddressId);
}

/// <summary>
/// Sends employee identity creation requests to the legacy Auth Service.
/// </summary>
/// <param name="httpClient">The service-authenticated HTTP client configured for the Auth Service.</param>
public sealed class EmployeeIdentityCreationClient(HttpClient httpClient) : IEmployeeIdentityCreationClient
{
    /// <inheritdoc />
    public async Task<HttpResponseMessage> CreateAsync(int employeeId, CreateEmployeeAccountRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/auth/v1/employee-identities/{employeeId}")
        {
            Content = JsonContent.Create(new EmployeeIdentityRequest(
                request.Email, request.Email, request.Password, true, request.PhoneNumber)),
        };
        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record EmployeeIdentityRequest(string UserName, string Email, string Password, bool EmailConfirmed, string? PhoneNumber);
}
