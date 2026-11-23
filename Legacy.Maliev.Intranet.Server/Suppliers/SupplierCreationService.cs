using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Normalized outcomes from supplier profile and address creation.</summary>
public enum SupplierCreationStatus
{
    /// <summary>The supplier and any eligible address were created.</summary>
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
    /// <summary>Creates a supplier and, when supplied, an address; ambiguous address failures require reconciliation.</summary>
    public async Task<SupplierCreationResult> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken)
    {
        if (request.HasAnyAddress() && !request.HasCreateAddress()) return new(SupplierCreationStatus.BadRequest);

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

        if (!request.HasCreateAddress()) return new(SupplierCreationStatus.Created, supplierId);

        try
        {
            using var addressResponse = await addresses.CreateAsync(supplierId, request, CancellationToken.None);
            if (addressResponse.IsSuccessStatusCode) return new(SupplierCreationStatus.Created, supplierId);
        }
        catch (OperationCanceledException)
        {
            // The address write may have committed before its response was lost.
        }
        catch (HttpRequestException)
        {
            // The address write may have committed before its response was lost.
        }
        logger.LogWarning("Supplier address creation requires reconciliation after an uncertain outcome.");
        return new(SupplierCreationStatus.Unavailable);
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
