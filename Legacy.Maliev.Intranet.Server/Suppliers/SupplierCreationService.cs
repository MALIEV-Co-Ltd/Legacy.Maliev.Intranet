using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Normalized outcomes from supplier profile and address creation.</summary>
public enum SupplierCreationStatus
{
    /// <summary>The supplier and address were created.</summary>
    Created,
    /// <summary>The downstream service rejected the request.</summary>
    BadRequest,
    /// <summary>The service identity was unauthenticated.</summary>
    Unauthorized,
    /// <summary>The service identity lacked permission.</summary>
    Forbidden,
    /// <summary>The supplier conflicts with an existing record.</summary>
    Conflict,
    /// <summary>The downstream service throttled the request.</summary>
    RateLimited,
    /// <summary>A successful response could not be decoded.</summary>
    BadGateway,
    /// <summary>The workflow could not complete safely.</summary>
    Unavailable,
}

/// <summary>Safe result returned to the BFF endpoint.</summary>
public sealed record SupplierCreationResult(SupplierCreationStatus Status, int? SupplierId = null, TimeSpan? RetryAfter = null);

/// <summary>Server-authenticated supplier profile write boundary.</summary>
public interface ISupplierProfileCreationClient
{
    /// <summary>Creates the supplier profile.</summary>
    Task<HttpResponseMessage> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes a supplier profile during compensation.</summary>
    Task<HttpResponseMessage> DeleteAsync(int supplierId, CancellationToken cancellationToken);
}

/// <summary>Server-authenticated supplier address write boundary.</summary>
public interface ISupplierAddressCreationClient
{
    /// <summary>Creates the address owned by a supplier.</summary>
    Task<HttpResponseMessage> CreateAsync(int supplierId, SupplierCreateRequest request, CancellationToken cancellationToken);
}

/// <summary>Owns the profile-plus-address transaction outside the thin BFF endpoint.</summary>
public sealed class SupplierCreationService(
    ISupplierProfileCreationClient profiles,
    ISupplierAddressCreationClient addresses,
    ILogger<SupplierCreationService> logger)
{
    /// <summary>Creates a supplier and address, compensating the profile when the address fails.</summary>
    public async Task<SupplierCreationResult> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage profileResponse;
        try
        {
            profileResponse = await profiles.CreateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SupplierCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(SupplierCreationStatus.Unavailable);
        }

        int supplierId;
        using (profileResponse)
        {
            if (!profileResponse.IsSuccessStatusCode)
            {
                return FromFailure(profileResponse);
            }

            try
            {
                var created = await profileResponse.Content.ReadFromJsonAsync<CreatedSupplier>(CancellationToken.None);
                if (created is null || created.Id <= 0)
                {
                    return new(SupplierCreationStatus.BadGateway);
                }

                supplierId = created.Id;
            }
            catch (System.Text.Json.JsonException)
            {
                return new(SupplierCreationStatus.BadGateway);
            }
        }

        SupplierCreationResult addressResult;
        try
        {
            using var addressResponse = await addresses.CreateAsync(supplierId, request, CancellationToken.None);
            addressResult = addressResponse.IsSuccessStatusCode
                ? new(SupplierCreationStatus.Created, supplierId)
                : FromFailure(addressResponse);
        }
        catch (OperationCanceledException)
        {
            addressResult = new(SupplierCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            addressResult = new(SupplierCreationStatus.Unavailable);
        }

        if (addressResult.Status == SupplierCreationStatus.Created)
        {
            return addressResult;
        }

        return await CompensateAsync(supplierId, addressResult)
            ? addressResult
            : new(SupplierCreationStatus.Unavailable);
    }

    private async Task<bool> CompensateAsync(int supplierId, SupplierCreationResult original)
    {
        try
        {
            using var response = await profiles.DeleteAsync(supplierId, CancellationToken.None);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            logger.LogError("Supplier compensation failed with HTTP {StatusCode} for supplier {SupplierId} after {WorkflowStatus}.", (int)response.StatusCode, supplierId, original.Status);
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogError(exception, "Supplier compensation was unavailable for supplier {SupplierId} after {WorkflowStatus}.", supplierId, original.Status);
            return false;
        }
    }

    private static SupplierCreationResult FromFailure(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.BadRequest => new(SupplierCreationStatus.BadRequest),
        HttpStatusCode.Unauthorized => new(SupplierCreationStatus.Unauthorized),
        HttpStatusCode.Forbidden => new(SupplierCreationStatus.Forbidden),
        HttpStatusCode.Conflict => new(SupplierCreationStatus.Conflict),
        HttpStatusCode.TooManyRequests => new(SupplierCreationStatus.RateLimited, RetryAfter: BoundedRetryAfter(response)),
        _ => new(SupplierCreationStatus.Unavailable),
    };

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        return delay is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromHours(1) ? value : null;
    }
}
