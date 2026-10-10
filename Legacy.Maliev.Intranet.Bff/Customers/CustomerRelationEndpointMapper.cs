using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

internal static partial class CustomerRelationEndpointMapper
{
    [GeneratedRegex("^\"[0-9a-f]{64}\"$", RegexOptions.CultureInvariant)]
    private static partial Regex RelationVersion();
    [GeneratedRegex("^\"[0-9a-fA-F]{8}\"$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomerVersion();

    private static bool ValidRoute(int id, string kind, string relation) => id > 0 &&
        (kind is "company" or "billing" or "shipping") &&
        (relation == "new" || int.TryParse(relation, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 && value.ToString(CultureInfo.InvariantCulture) == relation);

    internal static async Task<IResult> ReadAsync(int id, string kind, string relation,
        CustomerRelationClient client, HttpContext context, CancellationToken token)
    {
        if (!ValidRoute(id, kind, relation)) return Results.BadRequest();
        try
        {
            using var response = await client.ReadAsync(id, kind, relation, context, token);
            if (!response.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.MapWrite(response, context);
            if (response.StatusCode != HttpStatusCode.OK || !CopyVersions(response, context)) return CustomerUpdateEndpointMapper.InvalidResponse();
            var value = await response.Content.ReadFromJsonAsync<CustomerRelationDetail>(token);
            if (!ValidProjection(value, id, kind, relation)) return CustomerUpdateEndpointMapper.InvalidResponse();
            return Results.Ok(value);
        }
        catch (Exception error) when (IsBoundedFailure(error, token)) { return CustomerUpdateEndpointMapper.Unavailable(); }
    }

    internal static Task<IResult> SaveCompanyAsync(int id, string relation, CustomerCompanyEdit input,
        CustomerRelationClient client, HttpContext context, CancellationToken token) => SaveAsync(id, "company", relation, input, client, context, token);
    internal static Task<IResult> SaveAddressAsync(int id, string kind, string relation, CustomerAddressEdit input,
        CustomerRelationClient client, EmployeeAdministrationCountryClient countries, HttpContext context, CancellationToken token) => kind == "company"
            ? Task.FromResult<IResult>(Results.BadRequest()) : SaveAsync(id, kind, relation, input, client, context, token, countries);

    private static async Task<IResult> SaveAsync<T>(int id, string kind, string relation, T input,
        CustomerRelationClient client, HttpContext context, CancellationToken token, EmployeeAdministrationCountryClient? countries = null) where T : class
    {
        if (!ValidRoute(id, kind, relation)) return Results.BadRequest();
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new(input), errors, true)) return Results.BadRequest();
        if (!context.Request.Headers.ContainsKey("If-Match") || !context.Request.Headers.ContainsKey("X-Customer-If-Match")) return Results.StatusCode(428);
        if (!Single(context.Request.Headers["If-Match"], RelationVersion(), out var version) ||
            !Single(context.Request.Headers["X-Customer-If-Match"], CustomerVersion(), out var customerVersion)) return Results.BadRequest();
        try
        {
            using var permission = await client.CheckWritePermissionsAsync(kind, relation, context, token);
            if (!permission.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.MapWrite(permission, context);
            if (input is CustomerAddressEdit address)
            {
                if (countries is null) return CustomerUpdateEndpointMapper.Unavailable();
                using var references = await countries.ReadAsync(token);
                if (!references.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.Unavailable();
                var options = await references.Content.ReadFromJsonAsync<EmployeeAdministrationCountry[]>(token);
                if (!ValidCountries(options)) return CustomerUpdateEndpointMapper.InvalidResponse();
                if (!options!.Any(country => country.Id == address.CountryId)) return Results.BadRequest();
            }
            using var current = await client.ReadAsync(id, kind, relation, context, token);
            if (!current.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.MapWrite(current, context);
            if (current.StatusCode != HttpStatusCode.OK || !CopyVersions(current, context)) return CustomerUpdateEndpointMapper.InvalidResponse();
            var projection = await current.Content.ReadFromJsonAsync<CustomerRelationDetail>(token);
            if (!ValidProjection(projection, id, kind, relation)) return CustomerUpdateEndpointMapper.InvalidResponse();
            if (context.Response.Headers.ETag != version || context.Response.Headers["X-Customer-ETag"] != customerVersion)
                return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
            // Preserve the original browser validators; the producer closes any race after this read.
            using var response = await client.SaveAsync(id, kind, relation, input, version, customerVersion, context, token);
            if (!response.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.MapWrite(response, context);
            // A malformed success can follow a committed write. The client requires reload, never replay.
            if (response.StatusCode != HttpStatusCode.NoContent || !CopyVersions(response, context) ||
                !response.Headers.TryGetValues("X-Relation-Id", out var ids) || ids.ToArray() is not [var actual] ||
                !int.TryParse(actual, NumberStyles.None, CultureInfo.InvariantCulture, out var boundId) || boundId <= 0 ||
                relation != "new" && actual != relation) return CustomerUpdateEndpointMapper.InvalidResponse();
            context.Response.Headers["X-Relation-Id"] = actual;
            return Results.NoContent();
        }
        catch (Exception error) when (IsBoundedFailure(error, token)) { return CustomerUpdateEndpointMapper.Unavailable(); }
    }

    internal static async Task<IResult> CountriesAsync(EmployeeAdministrationCountryClient countries, HttpContext context, CancellationToken token)
    {
        try
        {
            using var response = await countries.ReadAsync(token);
            if (!response.IsSuccessStatusCode) return CustomerUpdateEndpointMapper.MapReadFailure(response, context)!;
            var values = await response.Content.ReadFromJsonAsync<EmployeeAdministrationCountry[]>(token);
            if (!ValidCountries(values))
                return CustomerUpdateEndpointMapper.InvalidResponse();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(values);
        }
        catch (Exception error) when (IsBoundedFailure(error, token)) { return CustomerUpdateEndpointMapper.Unavailable(); }
    }

    private static bool ValidCountries(EmployeeAdministrationCountry[]? values) => values is { Length: > 0 } &&
        values.All(value => value.Id > 0 && !string.IsNullOrWhiteSpace(value.Name)) && values.Select(value => value.Id).Distinct().Count() == values.Length;

    private static bool ValidProjection(CustomerRelationDetail? value, int id, string kind, string relation) =>
        !(value is null || value.CustomerId != id ||
                (relation == "new" ? value.RelationId is not null : value.RelationId != int.Parse(relation, CultureInfo.InvariantCulture)) ||
                (kind == "company" ? value.Address is not null || (relation == "new" ? value.Company is not null : value.Company is null || value.Company.Id != value.RelationId || string.IsNullOrWhiteSpace(value.Company.Name))
                    : value.Company is not null || (relation == "new" ? value.Address is not null : value.Address is null || value.Address.Id != value.RelationId || string.IsNullOrWhiteSpace(value.Address.AddressLine1) || value.Address.CountryId <= 0)));

    private static bool CopyVersions(HttpResponseMessage response, HttpContext context)
    {
        var version = response.Headers.ETag?.ToString();
        if (version is null || !RelationVersion().IsMatch(version) ||
            !response.Headers.TryGetValues("X-Customer-ETag", out var values) || values.ToArray() is not [var customer] || !CustomerVersion().IsMatch(customer)) return false;
        context.Response.Headers.ETag = version;
        context.Response.Headers["X-Customer-ETag"] = customer;
        context.Response.Headers.CacheControl = "no-store";
        return true;
    }

    private static bool IsBoundedFailure(Exception error, CancellationToken token) => error is IOException || CustomerUpdateEndpointMapper.IsBoundedFailure(error, token);

    private static bool Single(Microsoft.Extensions.Primitives.StringValues values, Regex pattern, out string value)
    {
        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return pattern.IsMatch(value);
    }
}
