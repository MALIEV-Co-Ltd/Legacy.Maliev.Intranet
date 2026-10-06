using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Employees;

/// <summary>A safe edit read without downstream bodies or credentials in failure responses.</summary>
public sealed record EmployeeAdministrationLoadResult(int StatusCode, EmployeeAdministrationEdit? Edit = null);

/// <summary>Restores identity, address, then profile ordering without compensation or automatic replay.</summary>
public sealed class EmployeeAdministrationCoordinator(
    EmployeeAdministrationIdentityClient identities, EmployeeAdministrationProfileClient profiles,
    EmployeeAdministrationCredentialSender credentials, EmployeeAdministrationCountryClient countries)
{
    /// <summary>Loads independent versions and derives address ownership from the target employee.</summary>
    public async Task<EmployeeAdministrationLoadResult> LoadAsync(int employeeId, HttpContext context, CancellationToken token)
    {
        if (employeeId <= 0) return new(StatusCodes.Status400BadRequest);
        try
        {
            using var profileResponse = await profiles.ReadAsync(employeeId, context, token);
            if (profileResponse.StatusCode != HttpStatusCode.OK) return new(SafeStatus(profileResponse));
            var profile = await profileResponse.Content.ReadFromJsonAsync<EmployeeAdministrationProfile>(token);
            var profileVersion = profileResponse.Headers.ETag?.ToString();
            if (profile is null || profile.Id != employeeId || !EmployeeAdministrationVersion.IsEmployee(profileVersion))
                return new(StatusCodes.Status502BadGateway);

            using var identityResponse = await identities.ReadAsync(employeeId, context, token);
            if (identityResponse.StatusCode != HttpStatusCode.OK) return new(SafeStatus(identityResponse));
            var identity = await identityResponse.Content.ReadFromJsonAsync<EmployeeAdministrationIdentity>(token);
            var identityVersion = identityResponse.Headers.ETag?.ToString();
            if (identity is null || identity.DatabaseID != employeeId || string.IsNullOrWhiteSpace(identity.Id)
                || !EmployeeAdministrationVersion.IsIdentity(identityVersion)
                || !string.Equals(identityVersion, '"' + identity.Version + '"', StringComparison.Ordinal))
                return new(StatusCodes.Status502BadGateway);

            EmployeeAddressDetail? address = null;
            string? addressVersion = null;
            if (profile.HomeAddressId is { } addressId)
            {
                if (addressId <= 0) return new(StatusCodes.Status502BadGateway);
                using var addressResponse = await profiles.ReadAddressAsync(addressId, context, token);
                if (addressResponse.StatusCode != HttpStatusCode.OK) return new(SafeStatus(addressResponse));
                address = await addressResponse.Content.ReadFromJsonAsync<EmployeeAddressDetail>(token);
                addressVersion = addressResponse.Headers.ETag?.ToString();
                if (address?.Id != addressId || !EmployeeAdministrationVersion.IsEmployee(addressVersion))
                    return new(StatusCodes.Status502BadGateway);
            }

            using var roleResponse = await profiles.ReadRolesAsync(context, token);
            IReadOnlyList<EmployeeRoleDetail> roles = [];
            if (roleResponse.StatusCode == HttpStatusCode.OK)
            {
                var projectedRoles = await roleResponse.Content.ReadFromJsonAsync<EmployeeRoleDetail[]>(token);
                if (projectedRoles is null) return new(StatusCodes.Status502BadGateway);
                roles = projectedRoles;
                if (roles.Any(role => role is null || role.Id <= 0) || roles.Select(role => role.Id).Distinct().Count() != roles.Count)
                    return new(StatusCodes.Status502BadGateway);
            }
            else if (roleResponse.StatusCode != HttpStatusCode.NotFound) return new(SafeStatus(roleResponse));

            using var countryResponse = await countries.ReadAsync(token);
            IReadOnlyList<EmployeeAdministrationCountry> countryOptions = [];
            if (countryResponse.StatusCode == HttpStatusCode.OK)
            {
                var projectedCountries = await countryResponse.Content.ReadFromJsonAsync<EmployeeAdministrationCountry[]>(token);
                if (projectedCountries is null || projectedCountries.Any(country => country is null
                    || country.Id <= 0 || string.IsNullOrWhiteSpace(country.Name))
                    || projectedCountries.Select(country => country.Id).Distinct().Count() != projectedCountries.Length)
                    return new(StatusCodes.Status502BadGateway);
                countryOptions = projectedCountries;
            }
            else if (countryResponse.StatusCode != HttpStatusCode.NotFound) return new(SafeStatus(countryResponse));
            return new(StatusCodes.Status200OK, new(profile, identity, address, roles,
                profileVersion!, identityVersion!, addressVersion, countryOptions));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            return new(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Stops on the first failure or uncertain acknowledgement, retaining confirmed steps.</summary>
    public async Task<EmployeeAdministrationSaveResult> SaveAsync(int employeeId,
        EmployeeAdministrationSaveRequest input, HttpContext context, CancellationToken token)
    {
        var completed = new List<string>();
        var stage = "validation";
        var dispatched = false;
        int? createdAddressId = null;
        if (!Validator.TryValidateObject(input, new ValidationContext(input), [], true)
            || !EmployeeAdministrationVersion.IsEmployee(input.ProfileVersion)
            || !EmployeeAdministrationVersion.IsIdentity(input.IdentityVersion)
            || (input.CapturedHomeAddressId is not null && !EmployeeAdministrationVersion.IsEmployee(input.AddressVersion)))
            return new(stage, [], false, StatusCodes.Status400BadRequest);
        try
        {
            // Preserve the source's optional new-address no-op when its required fields are absent.
            var addressToWrite = input.Address;
            if (input.CapturedHomeAddressId is null && addressToWrite is { } optionalAddress
                && (string.IsNullOrEmpty(optionalAddress.AddressLine1) || optionalAddress.CountryId == 0))
                addressToWrite = null;
            if (addressToWrite is not null && !Validator.TryValidateObject(addressToWrite,
                new ValidationContext(addressToWrite), [], true))
                return new(stage, [], false, StatusCodes.Status400BadRequest);
            var writePermissions = new List<string>
            {
                "legacy-auth.employee-identities.update", "legacy-employee.employees.update",
            };
            if (addressToWrite is not null)
                writePermissions.Add(input.CapturedHomeAddressId is null
                    ? "legacy-employee.addresses.create" : "legacy-employee.addresses.update");
            using var admission = await credentials.CheckPermissionsAsync(writePermissions, context, token);
            if (admission.StatusCode != HttpStatusCode.NoContent)
                return new(stage, [], false, SafeStatus(admission));
            var current = await LoadAsync(employeeId, context, token);
            if (current.Edit is not { } edit) return new(stage, [], false, current.StatusCode);
            // Fresh readback validates relationships only. It NEVER replaces the editor's versions.
            if (edit.Profile.HomeAddressId != input.CapturedHomeAddressId
                || !string.Equals(edit.ProfileVersion, input.ProfileVersion, StringComparison.Ordinal)
                || !string.Equals(edit.IdentityVersion, input.IdentityVersion, StringComparison.Ordinal)
                || !string.Equals(edit.AddressVersion, input.AddressVersion, StringComparison.Ordinal))
                return new(stage, [], false, StatusCodes.Status412PreconditionFailed);
            if (input.RoleId is { } roleId && !edit.Roles.Any(role => role.Id == roleId))
                return new(stage, [], false, StatusCodes.Status400BadRequest);
            if (addressToWrite is { } address && (string.IsNullOrWhiteSpace(address.AddressLine1)
                || !edit.Countries.Any(country => country.Id == address.CountryId)))
                return new(stage, [], false, StatusCodes.Status400BadRequest);
            if (input.CapturedHomeAddressId is not null && input.Address is null)
                return new(stage, [], false, StatusCodes.Status400BadRequest);

            stage = "identity";
            dispatched = true;
            // Restore the source's Email -> UserName rule. Preserve non-editable security settings.
            using (var response = await identities.UpdateAsync(employeeId,
                new(input.Email, input.Email, input.EmailConfirmed, input.PhoneNumber,
                    input.PhoneNumberConfirmed, edit.Identity.TwoFactorEnabled,
                    edit.Identity.LockoutEnd, input.LockoutEnabled), input.IdentityVersion, context, token))
            {
                if (response.StatusCode != HttpStatusCode.NoContent) return Failure(response);
            }
            completed.Add(stage);
            dispatched = false;

            var finalAddressId = edit.Profile.HomeAddressId;
            if (addressToWrite is { } addressInput)
            {
                stage = "address";
                dispatched = true;
                if (finalAddressId is { } addressId)
                {
                    using var response = await profiles.UpdateAddressAsync(employeeId, addressId, addressInput,
                        input.AddressVersion!, input.ProfileVersion, context, token);
                    if (response.StatusCode != HttpStatusCode.NoContent) return Failure(response);
                    if (!EmployeeAdministrationVersion.IsEmployee(response.Headers.ETag?.ToString())
                        || !response.Headers.TryGetValues("X-Employee-ETag", out var employeeVersions)
                        || !employeeVersions.SequenceEqual([input.ProfileVersion], StringComparer.Ordinal))
                        return new(stage, completed.Append(stage).ToArray(), false, StatusCodes.Status502BadGateway);
                }
                else
                {
                    using var response = await profiles.CreateAddressAsync(addressInput, context, token);
                    if (response.StatusCode != HttpStatusCode.Created) return Failure(response);
                    var created = await response.Content.ReadFromJsonAsync<EmployeeAddressDetail>(token);
                    if (created is not { Id: > 0 })
                        return new(stage, completed.ToArray(), true, StatusCodes.Status502BadGateway);
                    // Only this confirmed creation response may provide a new relation identifier.
                    finalAddressId = created.Id;
                    createdAddressId = created.Id;
                }
                completed.Add(stage);
                dispatched = false;
            }

            stage = "profile";
            dispatched = true;
            using (var response = await profiles.UpdateAsync(employeeId,
                new(input.RoleId, input.FirstName, input.LastName, input.PhoneNumber, input.Email,
                    input.DateOfBirth, finalAddressId), input.ProfileVersion, context, token))
            {
                if (response.StatusCode != HttpStatusCode.NoContent) return Failure(response);
                if (!EmployeeAdministrationVersion.IsEmployee(response.Headers.ETag?.ToString()))
                    return new(stage, completed.Append(stage).ToArray(), false, StatusCodes.Status502BadGateway, createdAddressId);
            }
            completed.Add(stage);
            return new("complete", completed.ToArray(), false, StatusCodes.Status200OK, createdAddressId);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            return new(stage, completed.ToArray(), dispatched, StatusCodes.Status503ServiceUnavailable, createdAddressId);
        }

        EmployeeAdministrationSaveResult Failure(HttpResponseMessage response) =>
            new(stage, completed.ToArray(), response is not EmployeeAdministrationCredentialRejection
                && (int)response.StatusCode is not (400 or 401 or 403 or 404 or 409 or 412 or 428 or 429),
                SafeStatus(response), createdAddressId);
    }

    private static int SafeStatus(HttpResponseMessage response) => (int)response.StatusCode switch
    {
        400 or 401 or 403 or 404 or 409 or 412 or 428 or 429 => (int)response.StatusCode,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
