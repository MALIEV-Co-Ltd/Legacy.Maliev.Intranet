using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Maps trusted session context into the Accounting receipt API without owning business rules.</summary>
public static class InvoiceReceiptEndpointMapper
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Creates or reconciles a receipt.</summary>
    public static Task<IResult> CreateAsync(
        int id,
        CreateInvoiceReceiptRequest input,
        HttpContext context,
        InvoiceDetailProxy invoices,
        CancellationToken cancellationToken) => ExecuteAsync(
            context,
            (employeeId, operationId, token) => invoices.CreateReceiptAsync(id, input, employeeId, operationId, token),
            cancellationToken);

    /// <summary>Removes a receipt.</summary>
    public static Task<IResult> RemoveAsync(
        int id,
        HttpContext context,
        InvoiceDetailProxy invoices,
        CancellationToken cancellationToken) => ExecuteAsync(
            context,
            (_, operationId, token) => invoices.RemoveReceiptAsync(id, operationId, token),
            cancellationToken,
            requireEmployee: false);

    /// <summary>Explicitly emails a receipt.</summary>
    public static Task<IResult> EmailAsync(
        int id,
        HttpContext context,
        InvoiceDetailProxy invoices,
        CancellationToken cancellationToken) => ExecuteAsync(
            context,
            (employeeId, operationId, token) => invoices.EmailReceiptAsync(id, employeeId, operationId, token),
            cancellationToken);

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        Func<int, Guid, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken,
        bool requireEmployee = true)
    {
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParseExact(key, "D", out var operationId) || operationId == Guid.Empty)
        {
            return Results.BadRequest();
        }

        var employeeText = context.User.FindFirst(EmployeeSessionService.LegacyDatabaseIdClaim)?.Value;
        var employeeId = 0;
        if (requireEmployee && (!int.TryParse(employeeText, NumberStyles.None, CultureInfo.InvariantCulture, out employeeId) || employeeId <= 0))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        HttpResponseMessage response;
        try
        {
            response = await send(requireEmployee ? employeeId : 0, operationId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            try
            {
                var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
                var result = JsonSerializer.Deserialize<InvoiceReceiptWorkflowResult>(bytes, Json);
                return result is null ? Results.StatusCode(StatusCodes.Status502BadGateway) : Results.Ok(result);
            }
            catch (JsonException)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumResponseBytes)
            {
                throw new JsonException("Accounting receipt response exceeded the safe limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
