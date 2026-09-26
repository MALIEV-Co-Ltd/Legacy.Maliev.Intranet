using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

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
public sealed record CustomerAccountCreationResult(
    CustomerAccountCreationStatus Status,
    int? CustomerId = null,
    TimeSpan? RetryAfter = null,
    string? OnboardingToken = null);

/// <summary>Server-authenticated CustomerService client used only by the account workflow.</summary>
public interface ICustomerProfileCreationClient
{
    /// <summary>Creates the customer profile using the exact legacy JSON contract.</summary>
    Task<HttpResponseMessage> CreateAsync(CreateCustomerAccountRequest request, Guid operationId, CancellationToken cancellationToken);

}

/// <summary>Server-authenticated AuthService client used only by the account workflow.</summary>
public interface ICustomerIdentityCreationClient
{
    /// <summary>Creates the customer identity for an already-created profile.</summary>
    Task<HttpResponseMessage> CreateAsync(int customerId, CreateCustomerAccountRequest request, string bootstrapSecret, CancellationToken cancellationToken);

    /// <summary>Creates or reconciles one service-owned identity operation.</summary>
    Task<HttpResponseMessage> ReconcileCreateAsync(int customerId, CreateCustomerAccountRequest request, string bootstrapSecret, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Creates a single-use setup challenge after the bootstrap identity has committed.</summary>
    Task<HttpResponseMessage> CreatePasswordSetupChallengeAsync(int customerId, CancellationToken cancellationToken);
}

/// <summary>Owns the legacy profile-plus-identity transaction outside the BFF endpoint.</summary>
public sealed class CustomerAccountCreationService(
    ICustomerProfileCreationClient profiles,
    ICustomerIdentityCreationClient identities,
    IOptions<CustomerIdentityReconciliationOptions> reconciliationOptions,
    ILogger<CustomerAccountCreationService> logger)
{
    /// <summary>Creates a customer profile and identity or returns a safe downstream outcome.</summary>
    public Task<CustomerAccountCreationResult> CreateAsync(
        CreateCustomerAccountRequest request,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        return CreateCoreAsync(request, operationId, cancellationToken);
    }

    private async Task<CustomerAccountCreationResult> CreateCoreAsync(
        CreateCustomerAccountRequest request,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var reconciliation = reconciliationOptions.Value;
        if (reconciliation.Enabled)
        {
            if (operationId == Guid.Empty)
            {
                return new(CustomerAccountCreationStatus.BadRequest);
            }

            try
            {
                reconciliation.Validate();
            }
            catch (InvalidOperationException)
            {
                logger.LogError("Customer identity reconciliation is enabled without a valid protected key.");
                return new(CustomerAccountCreationStatus.Unavailable);
            }
        }

        HttpResponseMessage profileResponse;
        try
        {
            profileResponse = await profiles.CreateAsync(request, operationId, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CustomerAccountCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Customer profile creation was unavailable.");
            return new(CustomerAccountCreationStatus.Unavailable);
        }

        int customerId;
        using (profileResponse)
        {
            if (!profileResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Customer profile creation returned HTTP {StatusCode}.",
                    (int)profileResponse.StatusCode);
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

        var bootstrapSecret = reconciliation.Enabled
            ? reconciliation.DeriveBootstrapSecret(operationId, customerId)
            : GenerateBootstrapSecret();
        CustomerAccountCreationResult identityResult;
        try
        {
            using var identityResponse = reconciliation.Enabled
                ? await identities.ReconcileCreateAsync(customerId, request, bootstrapSecret, operationId, CancellationToken.None)
                : await identities.CreateAsync(customerId, request, bootstrapSecret, CancellationToken.None);
            if (!identityResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Customer identity creation returned HTTP {StatusCode} for profile {CustomerId}.",
                    (int)identityResponse.StatusCode,
                    customerId);
                identityResult = FromFailure(identityResponse);
            }
            else
            {
                try
                {
                    if (reconciliation.Enabled)
                    {
                        var receipt = await identityResponse.Content.ReadFromJsonAsync<IdentityCreateReceipt>(CancellationToken.None);
                        var expectedStatus = identityResponse.StatusCode == HttpStatusCode.Created ? "created" :
                            identityResponse.StatusCode == HttpStatusCode.OK ? "replayed" : null;
                        identityResult = expectedStatus is not null && receipt is not null && receipt.DatabaseId == customerId &&
                            string.Equals(receipt.Status, expectedStatus, StringComparison.Ordinal)
                            ? await CreateOnboardingChallengeAsync(customerId)
                            : new(CustomerAccountCreationStatus.BadGateway);
                    }
                    else
                    {
                        var identity = await identityResponse.Content.ReadFromJsonAsync<CreatedIdentity>(CancellationToken.None);
                        identityResult = identity is not null && identity.DatabaseID == customerId
                            ? await CreateOnboardingChallengeAsync(customerId)
                            : new(CustomerAccountCreationStatus.BadGateway);
                    }
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
            logger.LogWarning("Customer identity creation was unavailable for profile {CustomerId}.", customerId);
            identityResult = new(CustomerAccountCreationStatus.Unavailable);
        }

        if (identityResult.Status == CustomerAccountCreationStatus.Created)
        {
            return identityResult;
        }

        // A keyed profile response may be a replay of an earlier committed create.
        // A failed or uncertain identity response does not prove that no identity was
        // committed; deleting the profile could orphan it even on the keyed route.
        logger.LogWarning(
            "Customer account {CustomerId} requires identity reconciliation after {WorkflowStatus}; profile was retained.",
            customerId,
            identityResult.Status);
        return identityResult with { CustomerId = customerId };
    }

    private async Task<CustomerAccountCreationResult> CreateOnboardingChallengeAsync(int customerId)
    {
        try
        {
            using var response = await identities.CreatePasswordSetupChallengeAsync(customerId, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Customer identity {CustomerId} was created but its onboarding challenge returned HTTP {StatusCode}.",
                    customerId,
                    (int)response.StatusCode);
                return new(CustomerAccountCreationStatus.Created, customerId);
            }

            var challenge = await response.Content.ReadFromJsonAsync<OnboardingChallenge>(CancellationToken.None);
            return challenge?.Accepted == true && !string.IsNullOrWhiteSpace(challenge.Token)
                ? new(CustomerAccountCreationStatus.Created, customerId, OnboardingToken: challenge.Token)
                : new(CustomerAccountCreationStatus.Created, customerId);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(
                "Customer identity {CustomerId} was created but its onboarding challenge was unavailable.",
                customerId);
            return new(CustomerAccountCreationStatus.Created, customerId);
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

    private static string GenerateBootstrapSecret()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%";
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        Span<char> password = stackalloc char[20];
        for (var index = 0; index < password.Length; index++)
        {
            password[index] = alphabet[bytes[index] % alphabet.Length];
        }

        return new string(password);
    }

    private sealed record CreatedProfile(int Id);
    private sealed record CreatedIdentity(int DatabaseID);
    private sealed record IdentityCreateReceipt(int DatabaseId, string Status);
    private sealed record OnboardingChallenge(bool Accepted, string? Token);
}
