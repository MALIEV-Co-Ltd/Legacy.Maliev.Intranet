using Microsoft.AspNetCore.Antiforgery;

namespace Legacy.Maliev.Intranet.Bff;

internal sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        return await next(context);
    }
}
