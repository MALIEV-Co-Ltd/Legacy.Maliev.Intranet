using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Employees;

// In-process marker: an external HTTP response cannot impersonate local pre-send rejection.
internal sealed class EmployeeAdministrationCredentialRejection(HttpStatusCode status) : HttpResponseMessage(status);

/// <summary>Reads only the existing public Catalog country list; no credential or grant is needed.</summary>
public sealed class EmployeeAdministrationCountryClient(HttpClient client)
{
    /// <summary>Uses the producer's anonymous reference route, without exposing service URLs to the browser.</summary>
    public Task<HttpResponseMessage> ReadAsync(CancellationToken token) => client.GetAsync("/Countries", token);
}

/// <summary>Forwards the acting employee's server-held credential, never workload credentials.</summary>
public sealed class EmployeeAdministrationCredentialSender(EmployeeSessionService sessions)
{
    /// <summary>Rejects known missing write permissions before starting a multi-service update.</summary>
    public async Task<HttpResponseMessage> CheckPermissionsAsync(
        IReadOnlyList<string> permissions, HttpContext context, CancellationToken token)
    {
        var acquired = await sessions.AcquireAccessTokenAsync(context, token);
        token.ThrowIfCancellationRequested();
        return Failure(acquired, permissions) ?? new(HttpStatusCode.NoContent);
    }

    /// <summary>Rechecks renewed permissions and sends exactly once; callers own the response.</summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, string permission,
        HttpContext context, CancellationToken token, string? additionalPermission = null)
    {
        using (request)
        {
            context.Response.Headers.CacheControl = "no-store";
            var acquired = await sessions.AcquireAccessTokenAsync(context, token);
            token.ThrowIfCancellationRequested();
            if (Failure(acquired, additionalPermission is null ? [permission] : [permission, additionalPermission]) is { } failure) return failure;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acquired.AccessToken);
            // These dedicated clients must have all inherited resilience handlers removed.
            // A lost acknowledgement is not evidence that the write did not commit.
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
        }
    }

    private static HttpResponseMessage? Failure(EmployeeAccessTokenAcquisition acquired, IReadOnlyList<string> permissions)
    {
        if (acquired.Status == EmployeeAccessTokenStatus.Throttled)
        {
            var response = new EmployeeAdministrationCredentialRejection(HttpStatusCode.TooManyRequests);
            if (acquired.RetryAfterSeconds is > 0 and <= 3600)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(acquired.RetryAfterSeconds.Value));
            return response;
        }
        if (acquired.Status == EmployeeAccessTokenStatus.Unavailable)
            return new EmployeeAdministrationCredentialRejection(HttpStatusCode.ServiceUnavailable);
        if (acquired.Status != EmployeeAccessTokenStatus.Available || string.IsNullOrWhiteSpace(acquired.AccessToken))
            return new EmployeeAdministrationCredentialRejection(HttpStatusCode.Unauthorized);
        return permissions.Any(permission => !acquired.Permissions.Contains(permission, StringComparer.Ordinal))
            ? new EmployeeAdministrationCredentialRejection(HttpStatusCode.Forbidden) : null;
    }
}

/// <summary>Dedicated Auth administration transport, with no fallback to a service principal.</summary>
public sealed class EmployeeAdministrationIdentityClient(
    HttpClient client, EmployeeAdministrationCredentialSender sender)
{
    /// <summary>Reads the safe identity and its independently captured Auth ETag.</summary>
    public Task<HttpResponseMessage> ReadAsync(int employeeId, HttpContext context, CancellationToken token) =>
        sender.SendAsync(client, new(HttpMethod.Get, $"/auth/v1/employee-identities/{employeeId}"),
            "legacy-auth.employee-identities.read", context, token);

    /// <summary>Sends only identity fields with the original quoted Auth version.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int employeeId,
        EmployeeAdministrationIdentityUpdate input, string version, HttpContext context, CancellationToken token)
    {
        if (!EmployeeAdministrationVersion.IsIdentity(version))
            throw new ArgumentException("A captured Auth identity version is required.", nameof(version));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/auth/v1/employee-identities/{employeeId}/versioned")
        {
            Content = JsonContent.Create(input),
        };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(version));
        return sender.SendAsync(client, request, "legacy-auth.employee-identities.update", context, token);
    }
}

/// <summary>Dedicated Employee scalar/address transport; versions remain resource-specific.</summary>
public sealed class EmployeeAdministrationProfileClient(
    HttpClient client, EmployeeAdministrationCredentialSender sender)
{
    private static readonly JsonSerializerOptions LegacyJson = new(JsonSerializerDefaults.General);

    /// <summary>Reads only employee-owned scalar fields and their captured ETag.</summary>
    public Task<HttpResponseMessage> ReadAsync(int employeeId, HttpContext context, CancellationToken token) =>
        SendReadAsync($"/employees/{employeeId}/edit", "legacy-employee.employees.read", context, token);

    /// <summary>Reads the address derived from the server-verified employee relation.</summary>
    public Task<HttpResponseMessage> ReadAddressAsync(int addressId, HttpContext context, CancellationToken token) =>
        SendReadAsync($"/employees/addresses/{addressId}", "legacy-employee.addresses.read", context, token);

    /// <summary>Reads available roles without introducing role mutation.</summary>
    public Task<HttpResponseMessage> ReadRolesAsync(HttpContext context, CancellationToken token) =>
        SendReadAsync("/employees/roles", "legacy-employee.roles.read", context, token);

    /// <summary>Writes profile fields using the captured employee version, not a refreshed version.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int employeeId,
        EmployeeAdministrationProfileUpdate input, string version, HttpContext context, CancellationToken token) =>
        SendWriteAsync(HttpMethod.Put, $"/employees/{employeeId}/versioned", input,
            "legacy-employee.employees.update", version, context, token);

    /// <summary>Fences the address version and its employee relationship in the producer transaction.</summary>
    public Task<HttpResponseMessage> UpdateAddressAsync(int employeeId, int addressId,
        EmployeeAdministrationAddressUpdate input, string version, string employeeVersion,
        HttpContext context, CancellationToken token)
    {
        if (employeeId <= 0 || addressId <= 0 || !EmployeeAdministrationVersion.IsEmployee(version)
            || !EmployeeAdministrationVersion.IsEmployee(employeeVersion))
            throw new ArgumentException("Captured employee and address versions are required.");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/employees/{employeeId}/home-address/versioned")
        {
            Content = JsonContent.Create(new EmployeeAdministrationBoundAddressUpdate(addressId,
                input.Building, input.AddressLine1, input.AddressLine2, input.City, input.State,
                input.PostalCode, input.CountryId), options: LegacyJson),
        };
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(version));
        request.Headers.Add("X-Employee-If-Match", employeeVersion);
        return sender.SendAsync(client, request, "legacy-employee.addresses.update", context, token,
            "legacy-employee.employees.update");
    }

    /// <summary>Creates once; an unconfirmed response must never trigger an automatic replay.</summary>
    public Task<HttpResponseMessage> CreateAddressAsync(EmployeeAdministrationAddressUpdate input,
        HttpContext context, CancellationToken token) =>
        SendWriteAsync(HttpMethod.Post, "/employees/addresses", input,
            "legacy-employee.addresses.create", null, context, token);

    private Task<HttpResponseMessage> SendReadAsync(string path, string permission,
        HttpContext context, CancellationToken token) =>
        sender.SendAsync(client, new(HttpMethod.Get, path), permission, context, token);

    private Task<HttpResponseMessage> SendWriteAsync<T>(HttpMethod method, string path, T input,
        string permission, string? version, HttpContext context, CancellationToken token)
    {
        if (method == HttpMethod.Put && !EmployeeAdministrationVersion.IsEmployee(version))
            throw new ArgumentException("A captured Employee resource version is required.", nameof(version));
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(input, options: LegacyJson),
        };
        if (version is not null) request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(version));
        return sender.SendAsync(client, request, permission, context, token);
    }
}
