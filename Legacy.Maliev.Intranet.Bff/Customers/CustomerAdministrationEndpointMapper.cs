using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Same-origin customer profile and identity administration with explicit partial-write receipts.</summary>
public static class CustomerAdministrationEndpointMapper
{
    /// <summary>Maps independently versioned reads and CSRF-protected ordered saves without changing existing routes.</summary>
    public static void MapCustomerAdministrationEndpoints(this WebApplication app)
    {
        app.MapGet("/bff/customers/{id:int}/edit", async (int id, HttpContext context,
            CustomerAdministrationCoordinator coordinator, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await coordinator.LoadAsync(id, context, token);
            return result.Edit is { } edit ? Results.Ok(edit) : Results.StatusCode(result.StatusCode);
        }).RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim("permissions", "legacy-customer.customers.read")
            .RequireClaim("permissions", "legacy-auth.customer-identities.read"));

        app.MapPut("/bff/customers/{id:int}/edit", async (int id, CustomerAdministrationSaveRequest input,
            HttpContext context, CustomerAdministrationCoordinator coordinator, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await coordinator.SaveAsync(id, input, context, token);
            return Results.Json(result, statusCode: result.StatusCode);
        }).AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireAuthorization(policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("permissions", "legacy-customer.customers.read")
                .RequireClaim("permissions", "legacy-customer.customers.update")
                .RequireClaim("permissions", "legacy-auth.customer-identities.read")
                .RequireClaim("permissions", "legacy-auth.customer-identities.update"));
    }
}
