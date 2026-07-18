namespace Legacy.Maliev.Intranet.Bff.Diagnostics;

/// <summary>Records only redacted failure metadata for requests handled by the employee BFF.</summary>
public sealed class DiagnosticEventMiddleware(RequestDelegate next)
{
    /// <summary>Invokes the request and records a sanitized event for failures.</summary>
    public async Task InvokeAsync(HttpContext context, DiagnosticEventStore store)
    {
        try
        {
            await next(context);
            if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                store.RecordResponseFailure(
                    context.Response.StatusCode,
                    context.Request.Path.Value,
                    context.TraceIdentifier);
            }
        }
        catch
        {
            store.RecordUnhandledFailure(
                context.Request.Path.Value,
                context.TraceIdentifier);
            throw;
        }
    }
}
