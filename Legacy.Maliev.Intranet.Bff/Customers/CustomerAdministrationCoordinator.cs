using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Safe joined-read outcome without downstream errors or credentials.</summary>
public sealed record CustomerAdministrationLoadResult(int StatusCode, CustomerAdministrationEdit? Edit = null);

/// <summary>Coordinates profile-first, identity-second writes without replay, compensation or cross-service atomicity claims.</summary>
public sealed class CustomerAdministrationCoordinator(
    CustomerAdministrationProfileClient profiles, CustomerAdministrationIdentityClient identities)
{
    private static readonly string[] ReadPermissions =
        ["legacy-customer.customers.read", "legacy-auth.customer-identities.read"];
    private static readonly string[] SavePermissions =
        ["legacy-customer.customers.read", "legacy-customer.customers.update",
            "legacy-auth.customer-identities.read", "legacy-auth.customer-identities.update"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };

    /// <summary>Loads only a matched Customer profile and its DatabaseID-bound Auth identity with independent versions.</summary>
    public async Task<CustomerAdministrationLoadResult> LoadAsync(int customerId, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (customerId <= 0) return new(StatusCodes.Status400BadRequest);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(LegacyPresentation.RequestTimeout);
            using var admission = await identities.CheckPermissionsAsync(ReadPermissions, context, timeout.Token);
            if (admission.StatusCode != HttpStatusCode.NoContent) return new(SafeStatus(admission, context));
            using var profileResponse = await profiles.ReadAsync(customerId, context, timeout.Token);
            if (profileResponse.StatusCode != HttpStatusCode.OK) return new(SafeStatus(profileResponse, context));
            var profileVersion = profileResponse.Headers.ETag?.ToString();
            var profile = await ReadAsync<CustomerDetail>(profileResponse, timeout.Token,
                "id", "firstName", "lastName", "fullName", "email");
            if (!ValidProfile(profile, customerId) || !CustomerAdministrationVersion.IsProfile(profileVersion))
                return new(StatusCodes.Status502BadGateway);
            using var identityResponse = await identities.ReadAsync(customerId, context, timeout.Token);
            if (identityResponse.StatusCode != HttpStatusCode.OK) return new(SafeStatus(identityResponse, context));
            var identityVersion = identityResponse.Headers.ETag?.ToString();
            var identity = await ReadAsync<CustomerAdministrationIdentity>(identityResponse, timeout.Token, "id", "databaseID", "version");
            if (!CustomerAdministrationVersion.IsBound(customerId, identity, identityVersion))
                return new(StatusCodes.Status502BadGateway);
            return new(StatusCodes.Status200OK, new(profile!, identity!, profileVersion!, identityVersion!));
        }
        catch (JsonException) { return new(StatusCodes.Status502BadGateway); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return new(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Preflights both original versions and all four permissions before the ordered, at-most-once application sends.</summary>
    public async Task<CustomerAdministrationSaveResult> SaveAsync(int customerId,
        CustomerAdministrationSaveRequest input, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        var stage = "validation";
        var completed = new List<string>();
        var dispatched = false;
        if (customerId <= 0 || input is null || !Validator.TryValidateObject(input, new(input), [], true)
            || !CustomerAdministrationVersion.IsProfile(input.ProfileVersion)
            || !CustomerAdministrationVersion.IsIdentity(input.IdentityVersion))
            return new(stage, [], false, StatusCodes.Status400BadRequest);
        try
        {
            stage = "preflight";
            using var admission = await identities.CheckPermissionsAsync(SavePermissions, context, token);
            if (admission.StatusCode != HttpStatusCode.NoContent)
                return new(stage, [], false, SafeStatus(admission, context));
            var read = await LoadAsync(customerId, context, token);
            if (read.Edit is not { } edit) return new(stage, [], false, read.StatusCode);
            // A fresh read proves binding only. Never replace either original browser version.
            if (!string.Equals(edit.ProfileVersion, input.ProfileVersion, StringComparison.Ordinal)
                || !string.Equals(edit.IdentityVersion, input.IdentityVersion, StringComparison.Ordinal))
                return new(stage, [], false, StatusCodes.Status412PreconditionFailed);
            var profileInput = input.ToProfile();
            var identityInput = input.ToIdentity(customerId, edit.Identity);
            if (!Validator.TryValidateObject(identityInput, new(identityInput), [], true))
                return new("validation", [], false, StatusCodes.Status400BadRequest);

            stage = "profile";
            dispatched = true;
            using (var response = await profiles.UpdateAsync(customerId, profileInput, edit.Profile,
                input.ProfileVersion, context, token))
            {
                if (response.StatusCode != HttpStatusCode.NoContent) return Failure(response);
            }
            completed.Add(stage);
            dispatched = false;
            stage = "identity";
            token.ThrowIfCancellationRequested();

            dispatched = true;
            using (var response = await identities.UpdateAsync(customerId, identityInput,
                input.IdentityVersion, context, token))
            {
                if (response.StatusCode != HttpStatusCode.NoContent) return Failure(response);
            }
            completed.Add(stage);
            return new("complete", completed.ToArray(), false, StatusCodes.Status200OK);
        }
        catch (JsonException)
        {
            return new(stage, completed.ToArray(), dispatched, StatusCodes.Status502BadGateway);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // Caller cancellation after dispatch cannot establish rollback. Never proceed to another write.
            return new(stage, completed.ToArray(), dispatched, StatusCodes.Status503ServiceUnavailable);
        }

        CustomerAdministrationSaveResult Failure(HttpResponseMessage response) => new(stage, completed.ToArray(),
            response is not CustomerAdministrationCredentialRejection && !ExplicitRejection(response.StatusCode),
            SafeStatus(response, context));
    }

    private static bool ExplicitRejection(HttpStatusCode status) => (int)status is 400 or 401 or 403 or 404 or 412 or 428;

    private static int SafeStatus(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter?.Delta is { } delay
            && delay.TotalSeconds is > 0 and <= 3600)
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 409 or 412 or 428 or 429 => (int)response.StatusCode,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
    }

    private static bool ValidProfile(CustomerDetail? profile, int id) =>
        profile is not null && profile.Id == id && !string.IsNullOrWhiteSpace(profile.FirstName)
        && !string.IsNullOrWhiteSpace(profile.LastName) && !string.IsNullOrWhiteSpace(profile.FullName)
        && !string.IsNullOrWhiteSpace(profile.Email)
        && profile.CompanyId is not <= 0 && profile.BillingAddressId is not <= 0 && profile.ShippingAddressId is not <= 0
        && (profile.Company is null || profile.Company.Id == profile.CompanyId)
        && (profile.BillingAddress is null || profile.BillingAddress.Id == profile.BillingAddressId)
        && (profile.ShippingAddress is null || profile.ShippingAddress.Id == profile.ShippingAddressId);

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken token, params string[] required)
    {
        // Transport buffers under its deadline. Bound parsing independently; DI owns the transport buffer limit.
        var data = await response.Content.ReadAsByteArrayAsync(token);
        if (data.Length > 64 * 1024) throw new JsonException("Administrative projection exceeds its bound.");
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Administrative projection must be an object.");
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!members.Add(property.Name)) throw new JsonException("Ambiguous administrative projection.");
        if (required.Any(name => !members.Contains(name))) throw new JsonException("Incomplete administrative projection.");
        token.ThrowIfCancellationRequested();
        return document.RootElement.Deserialize<T>(JsonOptions);
    }
}
