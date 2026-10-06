using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Employees;

/// <summary>Same-origin administrative employee editing with cookie permissions and CSRF protection.</summary>
public static class EmployeeAdministrationEndpointMapper
{
    /// <summary>Maps safe reads and the ordered administrative save; no service grants are added.</summary>
    public static void MapEmployeeAdministrationEndpoints(this WebApplication app)
    {
        app.MapGet("/bff/employees/{id:int}/edit", async (int id, HttpContext context,
            EmployeeAdministrationCoordinator coordinator, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await coordinator.LoadAsync(id, context, token);
            return result.Edit is { } edit ? Results.Ok(edit) : Results.StatusCode(result.StatusCode);
        }).RequireAuthorization(LegacyEmployeePermissions.EmployeesRead);

        app.MapPut("/bff/employees/{id:int}/edit", async (int id, EmployeeAdministrationSaveRequest input,
            HttpContext context, EmployeeAdministrationCoordinator coordinator, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await coordinator.SaveAsync(id, input, context, token);
            return Results.Json(result, statusCode: result.StatusCode);
        }).AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireAuthorization(LegacyEmployeePermissions.EmployeesUpdate,
                LegacyEmployeePermissions.EmployeeIdentitiesUpdate);
    }
}
