using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Employees;

/// <summary>
/// Identifies the externally observable outcome of the employee account creation workflow.
/// </summary>
public enum EmployeeAccountCreationStatus
{
    /// <summary>The employee profile and authentication identity were created successfully.</summary>
    Created,

    /// <summary>The submitted account details failed downstream validation.</summary>
    BadRequest,

    /// <summary>The downstream service rejected the service identity as unauthenticated.</summary>
    Unauthorized,

    /// <summary>The downstream service identity lacks permission to create the requested resource.</summary>
    Forbidden,

    /// <summary>An employee profile or identity already conflicts with the submitted details.</summary>
    Conflict,

    /// <summary>A downstream service temporarily rate-limited the creation request.</summary>
    RateLimited,

    /// <summary>A downstream success response did not contain a valid matching resource identifier.</summary>
    BadGateway,

    /// <summary>The workflow could not complete safely because a downstream operation was unavailable.</summary>
    Unavailable
}

/// <summary>
/// Describes the result returned after attempting the profile-and-identity employee account workflow.
/// </summary>
/// <param name="Status">The overall workflow outcome.</param>
/// <param name="EmployeeId">The created employee profile identifier when creation succeeds.</param>
/// <param name="RetryAfter">The bounded delay suggested by a rate-limited downstream service.</param>
public sealed record EmployeeAccountCreationResult(EmployeeAccountCreationStatus Status, int? EmployeeId = null, TimeSpan? RetryAfter = null);

/// <summary>
/// Defines the Employee Service operations required to create and compensate employee profiles.
/// </summary>
public interface IEmployeeProfileCreationClient
{
    /// <summary>Creates the employee profile that precedes authentication identity creation.</summary>
    /// <param name="request">The validated employee account details used to populate the profile.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The unbuffered response from the Employee Service.</returns>
    Task<HttpResponseMessage> CreateAsync(CreateEmployeeAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes an employee profile to compensate for a failed identity creation step.</summary>
    /// <param name="employeeId">The unique identifier of the employee profile to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The unbuffered response from the Employee Service.</returns>
    Task<HttpResponseMessage> DeleteAsync(int employeeId, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the Auth Service operation required to attach an authentication identity to an employee profile.
/// </summary>
public interface IEmployeeIdentityCreationClient
{
    /// <summary>Creates login credentials linked to an existing employee profile.</summary>
    /// <param name="employeeId">The unique identifier of the employee profile that owns the identity.</param>
    /// <param name="request">The validated employee account details containing the login credentials.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The unbuffered response from the Auth Service.</returns>
    Task<HttpResponseMessage> CreateAsync(int employeeId, CreateEmployeeAccountRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates profile-first employee account creation and compensating profile deletion when identity creation fails.
/// </summary>
/// <param name="profiles">The Employee Service client used for profile creation and compensation.</param>
/// <param name="identities">The Auth Service client used to create the linked employee identity.</param>
/// <param name="logger">The logger used to record compensation failures that require operational attention.</param>
public sealed class EmployeeAccountCreationService(
    IEmployeeProfileCreationClient profiles,
    IEmployeeIdentityCreationClient identities,
    ILogger<EmployeeAccountCreationService> logger)
{
    /// <summary>
    /// Creates an employee profile followed by its authentication identity, deleting the profile if the identity step fails.
    /// </summary>
    /// <param name="request">The validated profile and login details for the new employee.</param>
    /// <param name="cancellationToken">Token to cancel the profile creation step.</param>
    /// <returns>The normalized workflow outcome, including the employee identifier on success.</returns>
    public async Task<EmployeeAccountCreationResult> CreateAsync(CreateEmployeeAccountRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage profileResponse;
        try { profileResponse = await profiles.CreateAsync(request, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(EmployeeAccountCreationStatus.Unavailable); }
        catch (HttpRequestException) { return new(EmployeeAccountCreationStatus.Unavailable); }

        int employeeId;
        using (profileResponse)
        {
            if (!profileResponse.IsSuccessStatusCode) return FromFailure(profileResponse);
            try
            {
                var created = await profileResponse.Content.ReadFromJsonAsync<CreatedProfile>(CancellationToken.None);
                if (created is null || created.Id <= 0) return new(EmployeeAccountCreationStatus.BadGateway);
                employeeId = created.Id;
            }
            catch (System.Text.Json.JsonException) { return new(EmployeeAccountCreationStatus.BadGateway); }
        }

        EmployeeAccountCreationResult identityResult;
        try
        {
            using var identityResponse = await identities.CreateAsync(employeeId, request, CancellationToken.None);
            if (!identityResponse.IsSuccessStatusCode) identityResult = FromFailure(identityResponse);
            else
            {
                try
                {
                    var identity = await identityResponse.Content.ReadFromJsonAsync<CreatedIdentity>(CancellationToken.None);
                    identityResult = identity is not null && identity.DatabaseID == employeeId
                        ? new(EmployeeAccountCreationStatus.Created, employeeId)
                        : new(EmployeeAccountCreationStatus.BadGateway);
                }
                catch (System.Text.Json.JsonException) { identityResult = new(EmployeeAccountCreationStatus.BadGateway); }
            }
        }
        catch (OperationCanceledException) { identityResult = new(EmployeeAccountCreationStatus.Unavailable); }
        catch (HttpRequestException) { identityResult = new(EmployeeAccountCreationStatus.Unavailable); }

        if (identityResult.Status == EmployeeAccountCreationStatus.Created) return identityResult;
        return await CompensateAsync(employeeId, identityResult, CancellationToken.None)
            ? identityResult
            : new(EmployeeAccountCreationStatus.Unavailable);
    }

    private async Task<bool> CompensateAsync(int employeeId, EmployeeAccountCreationResult original, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await profiles.DeleteAsync(employeeId, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return true;
            logger.LogError("Employee profile compensation failed with HTTP {StatusCode} for profile {EmployeeId} after {WorkflowStatus}.", (int)response.StatusCode, employeeId, original.Status);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Employee profile compensation timed out for profile {EmployeeId} after {WorkflowStatus}.", employeeId, original.Status);
            return false;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Employee profile compensation was unavailable for profile {EmployeeId} after {WorkflowStatus}.", employeeId, original.Status);
            return false;
        }
    }

    private static EmployeeAccountCreationResult FromFailure(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.BadRequest => new(EmployeeAccountCreationStatus.BadRequest),
        HttpStatusCode.Unauthorized => new(EmployeeAccountCreationStatus.Unauthorized),
        HttpStatusCode.Forbidden => new(EmployeeAccountCreationStatus.Forbidden),
        HttpStatusCode.Conflict => new(EmployeeAccountCreationStatus.Conflict),
        HttpStatusCode.TooManyRequests => new(EmployeeAccountCreationStatus.RateLimited, RetryAfter: BoundedRetryAfter(response)),
        _ => new(EmployeeAccountCreationStatus.Unavailable),
    };

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1) ? retryAfter : null;
    }

    private sealed record CreatedProfile(int Id);
    private sealed record CreatedIdentity(int DatabaseID);
}
