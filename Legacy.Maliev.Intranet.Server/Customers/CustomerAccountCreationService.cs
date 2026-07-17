using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Customers;

/// <summary>Result categories exposed by the server-side customer account workflow.</summary>
public enum CustomerAccountCreationStatus
{
    /// <summary>Both profile and identity were created.</summary>
    Created,
    /// <summary>A downstream service rejected the validated request.</summary>
    BadRequest,
    /// <summary>The machine identity was not authenticated downstream.</summary>
    Unauthorized,
    /// <summary>The machine identity lacks a required downstream permission.</summary>
    Forbidden,
    /// <summary>The identity or profile already exists.</summary>
    Conflict,
    /// <summary>A downstream service throttled the workflow.</summary>
    RateLimited,
    /// <summary>A successful downstream response could not be decoded safely.</summary>
    BadGateway,
    /// <summary>A downstream service was unavailable.</summary>
    Unavailable,
}

/// <summary>Safe workflow result returned to the thin BFF endpoint.</summary>
public sealed record CustomerAccountCreationResult(CustomerAccountCreationStatus Status, int? CustomerId = null, TimeSpan? RetryAfter = null);

/// <summary>Server-authenticated CustomerService client used only by the account workflow.</summary>
public interface ICustomerProfileCreationClient
{
    /// <summary>Creates the customer profile using the exact legacy JSON contract.</summary>
    Task<HttpResponseMessage> CreateAsync(CreateCustomerAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a profile when the following identity step fails.</summary>
    Task<HttpResponseMessage> DeleteAsync(int customerId, CancellationToken cancellationToken);
}

/// <summary>Server-authenticated AuthService client used only by the account workflow.</summary>
public interface ICustomerIdentityCreationClient
{
    /// <summary>Creates the customer identity for an already-created profile.</summary>
    Task<HttpResponseMessage> CreateAsync(int customerId, CreateCustomerAccountRequest request, CancellationToken cancellationToken);
}

/// <summary>Owns the legacy profile-plus-identity transaction outside the BFF endpoint.</summary>
public sealed class CustomerAccountCreationService(
    ICustomerProfileCreationClient profiles,
    ICustomerIdentityCreationClient identities,
    ILogger<CustomerAccountCreationService> logger)
{
    /// <summary>Creates a customer profile and identity or returns a safe downstream outcome.</summary>
    public Task<CustomerAccountCreationResult> CreateAsync(
        CreateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        return CreateCoreAsync(request, cancellationToken);
    }

    private async Task<CustomerAccountCreationResult> CreateCoreAsync(
        CreateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage profileResponse;
        try
        {
            profileResponse = await profiles.CreateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CustomerAccountCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(CustomerAccountCreationStatus.Unavailable);
        }

        int customerId;
        using (profileResponse)
        {
            if (!profileResponse.IsSuccessStatusCode)
            {
                return FromFailure(profileResponse);
            }

            try
            {
                var created = await profileResponse.Content.ReadFromJsonAsync<CreatedProfile>(CancellationToken.None);
                if (created is null || created.Id <= 0)
                {
                    return new(CustomerAccountCreationStatus.BadGateway);
                }

                customerId = created.Id;
            }
            catch (System.Text.Json.JsonException)
            {
                return new(CustomerAccountCreationStatus.BadGateway);
            }
        }

        CustomerAccountCreationResult identityResult;
        try
        {
            using var identityResponse = await identities.CreateAsync(customerId, request, CancellationToken.None);
            if (!identityResponse.IsSuccessStatusCode)
            {
                identityResult = FromFailure(identityResponse);
            }
            else
            {
                try
                {
                    var identity = await identityResponse.Content.ReadFromJsonAsync<CreatedIdentity>(CancellationToken.None);
                    identityResult = identity is not null && identity.DatabaseID == customerId
                        ? new(CustomerAccountCreationStatus.Created, customerId)
                        : new(CustomerAccountCreationStatus.BadGateway);
                }
                catch (System.Text.Json.JsonException)
                {
                    identityResult = new(CustomerAccountCreationStatus.BadGateway);
                }
            }
        }
        catch (OperationCanceledException)
        {
            identityResult = new(CustomerAccountCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            identityResult = new(CustomerAccountCreationStatus.Unavailable);
        }

        if (identityResult.Status == CustomerAccountCreationStatus.Created)
        {
            return identityResult;
        }

        return await CompensateAsync(customerId, identityResult, CancellationToken.None)
            ? identityResult
            : new(CustomerAccountCreationStatus.Unavailable);
    }

    private async Task<bool> CompensateAsync(
        int customerId,
        CustomerAccountCreationResult originalResult,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await profiles.DeleteAsync(customerId, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            logger.LogError(
                "Customer profile compensation failed with HTTP {StatusCode} for profile {CustomerId} after {WorkflowStatus}.",
                (int)response.StatusCode,
                customerId,
                originalResult.Status);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                "Customer profile compensation timed out for profile {CustomerId} after {WorkflowStatus}.",
                customerId,
                originalResult.Status);
            return false;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(
                exception,
                "Customer profile compensation was unavailable for profile {CustomerId} after {WorkflowStatus}.",
                customerId,
                originalResult.Status);
            return false;
        }
    }

    private static CustomerAccountCreationResult FromFailure(HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new(CustomerAccountCreationStatus.BadRequest),
            HttpStatusCode.Unauthorized => new(CustomerAccountCreationStatus.Unauthorized),
            HttpStatusCode.Forbidden => new(CustomerAccountCreationStatus.Forbidden),
            HttpStatusCode.Conflict => new(CustomerAccountCreationStatus.Conflict),
            HttpStatusCode.TooManyRequests => new(
                CustomerAccountCreationStatus.RateLimited,
                RetryAfter: BoundedRetryAfter(response)),
            _ => new(CustomerAccountCreationStatus.Unavailable),
        };

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1)
            ? retryAfter
            : null;
    }

    private sealed record CreatedProfile(int Id);
    private sealed record CreatedIdentity(int DatabaseID);
}
