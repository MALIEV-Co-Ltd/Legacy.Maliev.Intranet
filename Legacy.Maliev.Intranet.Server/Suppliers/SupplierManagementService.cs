using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Normalized supplier management outcomes.</summary>
public enum SupplierManagementStatus
{
    /// <summary>The operation succeeded.</summary>
    Success,
    /// <summary>The supplier was not found.</summary>
    NotFound,
    /// <summary>The downstream service rejected the request.</summary>
    BadRequest,
    /// <summary>The service identity was unauthenticated.</summary>
    Unauthorized,
    /// <summary>The service identity lacked permission.</summary>
    Forbidden,
    /// <summary>The change conflicts with another record.</summary>
    Conflict,
    /// <summary>The downstream service throttled the request.</summary>
    RateLimited,
    /// <summary>A successful response was invalid.</summary>
    BadGateway,
    /// <summary>The downstream service was unavailable.</summary>
    Unavailable,
}

/// <summary>Safe supplier management result.</summary>
public sealed record SupplierManagementResult(SupplierManagementStatus Status, SupplierDetail? Supplier = null, TimeSpan? RetryAfter = null);

/// <summary>Server-authenticated ProcurementService supplier management boundary.</summary>
public interface ISupplierManagementClient
{
    /// <summary>Gets a supplier profile.</summary>
    Task<HttpResponseMessage> GetProfileAsync(int id, CancellationToken cancellationToken);
    /// <summary>Gets its owned address.</summary>
    Task<HttpResponseMessage> GetAddressAsync(int id, CancellationToken cancellationToken);
    /// <summary>Updates the profile.</summary>
    Task<HttpResponseMessage> UpdateProfileAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken);
    /// <summary>Creates the owned address.</summary>
    Task<HttpResponseMessage> CreateAddressAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken);
    /// <summary>Updates an owned address.</summary>
    Task<HttpResponseMessage> UpdateAddressAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes the profile.</summary>
    Task<HttpResponseMessage> DeleteProfileAsync(int id, CancellationToken cancellationToken);
}

/// <summary>Owns supplier detail, address-upsert, and delete behavior outside the thin BFF.</summary>
public sealed class SupplierManagementService(ISupplierManagementClient client)
{
    /// <summary>Loads and combines a supplier profile with its owned address.</summary>
    public async Task<SupplierManagementResult> GetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return new(SupplierManagementStatus.BadRequest);
        }

        try
        {
            using var profileResponse = await client.GetProfileAsync(id, cancellationToken);
            if (!profileResponse.IsSuccessStatusCode) return Failure(profileResponse);
            var profile = await profileResponse.Content.ReadFromJsonAsync<Profile>(cancellationToken);
            if (profile is null || profile.Id != id || string.IsNullOrWhiteSpace(profile.Name)) return new(SupplierManagementStatus.BadGateway);
            using var addressResponse = await client.GetAddressAsync(id, cancellationToken);
            Address? address = null;
            if (addressResponse.StatusCode != HttpStatusCode.NotFound)
            {
                if (!addressResponse.IsSuccessStatusCode) return Failure(addressResponse);
                address = await addressResponse.Content.ReadFromJsonAsync<Address>(cancellationToken);
                if (address is null || address.Id <= 0) return new(SupplierManagementStatus.BadGateway);
            }
            return new(SupplierManagementStatus.Success, new SupplierDetail(profile.Id, profile.Name, profile.Website, profile.TaxNumber, profile.Email, profile.Note, profile.Telephone, profile.Mobile, profile.Fax, address?.Building, address?.Address1 ?? string.Empty, address?.Address2, address?.City, address?.State, address?.PostalCode, address?.CountryId ?? 0));
        }
        catch (System.Text.Json.JsonException) { return new(SupplierManagementStatus.BadGateway); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(SupplierManagementStatus.Unavailable); }
        catch (HttpRequestException) { return new(SupplierManagementStatus.Unavailable); }
    }

    /// <summary>Updates the profile and creates or updates its owned address.</summary>
    public async Task<SupplierManagementResult> UpdateAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return new(SupplierManagementStatus.BadRequest);
        }

        try
        {
            using var addressLookup = await client.GetAddressAsync(id, cancellationToken);
            Address? address = null;
            if (addressLookup.StatusCode != HttpStatusCode.NotFound)
            {
                if (!addressLookup.IsSuccessStatusCode) return Failure(addressLookup);
                address = await addressLookup.Content.ReadFromJsonAsync<Address>(cancellationToken);
                if (address is null || address.Id <= 0) return new(SupplierManagementStatus.BadGateway);
            }
            using var profileResponse = await client.UpdateProfileAsync(id, request, cancellationToken);
            if (!profileResponse.IsSuccessStatusCode) return Failure(profileResponse);
            using var addressResponse = address is null
                ? await client.CreateAddressAsync(id, request, cancellationToken)
                : await client.UpdateAddressAsync(address.Id, request, cancellationToken);
            return addressResponse.IsSuccessStatusCode ? new(SupplierManagementStatus.Success) : Failure(addressResponse);
        }
        catch (System.Text.Json.JsonException) { return new(SupplierManagementStatus.BadGateway); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(SupplierManagementStatus.Unavailable); }
        catch (HttpRequestException) { return new(SupplierManagementStatus.Unavailable); }
    }

    /// <summary>Deletes a supplier profile.</summary>
    public async Task<SupplierManagementResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return new(SupplierManagementStatus.BadRequest);
        }

        try { using var response = await client.DeleteProfileAsync(id, cancellationToken); return response.IsSuccessStatusCode ? new(SupplierManagementStatus.Success) : Failure(response); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(SupplierManagementStatus.Unavailable); }
        catch (HttpRequestException) { return new(SupplierManagementStatus.Unavailable); }
    }

    private static SupplierManagementResult Failure(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.NotFound => new(SupplierManagementStatus.NotFound),
        HttpStatusCode.BadRequest => new(SupplierManagementStatus.BadRequest),
        HttpStatusCode.Unauthorized => new(SupplierManagementStatus.Unauthorized),
        HttpStatusCode.Forbidden => new(SupplierManagementStatus.Forbidden),
        HttpStatusCode.Conflict => new(SupplierManagementStatus.Conflict),
        HttpStatusCode.TooManyRequests => new(SupplierManagementStatus.RateLimited, RetryAfter: BoundedRetryAfter(response)),
        _ => new(SupplierManagementStatus.Unavailable),
    };

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        return delay is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromHours(1)
            ? value
            : null;
    }

    private sealed record Profile(int Id, string Name, string? Website, string? TaxNumber, string? Email, string? Note, string? Telephone, string? Mobile, string? Fax);
    private sealed record Address(int Id, string? Building, string? Address1, string? Address2, string? City, string? State, string? PostalCode, int CountryId);
}
