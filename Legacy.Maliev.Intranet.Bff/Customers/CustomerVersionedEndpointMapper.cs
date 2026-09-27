using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

internal static partial class CustomerVersionedEndpointMapper
{
    [GeneratedRegex("^\"[0-9a-fA-F]{8}\"$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionPattern();

    internal static async Task<IResult> GetAsync(
        int id, CustomersProxy customers, HttpContext context, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();

        try
        {
            using var response = await customers.GetVersionedByIdAsync(id, cancellationToken);
            var failure = CustomerUpdateEndpointMapper.MapReadFailure(response, context);
            if (failure is not null) return failure;
            if (!TryRevision(response, out var revision)) return CustomerUpdateEndpointMapper.InvalidResponse();

            var customer = await response.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
            if (!ValidVersionedCustomer(customer, id))
                return CustomerUpdateEndpointMapper.InvalidResponse();

            context.Response.Headers.ETag = revision;
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(customer);
        }
        catch (Exception exception) when (CustomerUpdateEndpointMapper.IsBoundedFailure(exception, cancellationToken))
        {
            return CustomerUpdateEndpointMapper.Unavailable();
        }
    }

    internal static async Task<IResult> UpdateAsync(
        int id, CustomerUpdateRequest input, CustomersProxy customers,
        CustomerUpdateProxy updates, HttpContext context, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        var invalidInput = CustomerUpdateEndpointMapper.ValidateInput(input);
        if (invalidInput is not null) return invalidInput;
        if (!context.Request.Headers.TryGetValue("If-Match", out var values))
            return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
        if (values.Count != 1 || values[0] is not { } revision || !RevisionPattern().IsMatch(revision))
            return Results.BadRequest();

        try
        {
            // The current projection supplies relation IDs only. Never replace the browser's
            // revision with this newer read's revision: the producer must reject a stale edit.
            using var currentResponse = await customers.GetVersionedByIdAsync(id, cancellationToken);
            var failure = CustomerUpdateEndpointMapper.MapReadFailure(currentResponse, context);
            if (failure is not null) return failure;
            if (!TryRevision(currentResponse, out _)) return CustomerUpdateEndpointMapper.InvalidResponse();
            var current = await currentResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
            if (!ValidVersionedCustomer(current, id))
                return CustomerUpdateEndpointMapper.InvalidResponse();

            using var updateResponse = await updates.UpdateVersionedAsync(id, input, current!, revision, cancellationToken);
            return CustomerUpdateEndpointMapper.MapWrite(updateResponse, context);
        }
        catch (Exception exception) when (CustomerUpdateEndpointMapper.IsBoundedFailure(exception, cancellationToken))
        {
            return CustomerUpdateEndpointMapper.Unavailable();
        }
    }

    private static bool TryRevision(HttpResponseMessage response, out string revision)
    {
        revision = response.Headers.ETag?.ToString() ?? string.Empty;
        return RevisionPattern().IsMatch(revision);
    }

    private static bool ValidVersionedCustomer(CustomerDetail? customer, int id) =>
        CustomerUpdateEndpointMapper.ValidCustomer(customer, id) &&
        (customer!.Company is null || !string.IsNullOrWhiteSpace(customer.Company.Name)) &&
        (customer.BillingAddress is null || !string.IsNullOrWhiteSpace(customer.BillingAddress.AddressLine1)) &&
        (customer.ShippingAddress is null || !string.IsNullOrWhiteSpace(customer.ShippingAddress.AddressLine1));
}
