using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Validates browser operation identity and preserves Accounting's bounded response contract.</summary>
public static class InvoiceCreationEndpointMapper
{
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Returns a bounded Accounting preview.</summary>
    public static Task<IResult> PreviewAsync(int quotationId, InvoiceCreationProxy invoices, CancellationToken cancellationToken) =>
        ExecuteAsync<InvoiceCreationPreview>(token => invoices.PreviewAsync(quotationId, token), cancellationToken);

    /// <summary>Validates and forwards one replay-safe creation attempt.</summary>
    public static async Task<IResult> CreateAsync(
        int quotationId, CreateInvoiceFromQuotationRequest input, HttpContext context,
        InvoiceCreationProxy invoices, EmployeeSessionService sessions,
        IInvoiceDelegationClient delegations, IOptions<InvoiceDelegationOptions> options,
        CancellationToken cancellationToken)
    {
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (quotationId <= 0 || !Guid.TryParseExact(key, "D", out var operationId) || operationId == Guid.Empty)
            return Results.BadRequest();

        string? delegation = null;
        if (options.Value.Enabled)
        {
            var employeeAccessToken = await sessions.GetAccessTokenAsync(context, cancellationToken);
            if (string.IsNullOrWhiteSpace(employeeAccessToken)) return Results.Unauthorized();
            try
            {
                delegation = await delegations.IssueAsync(employeeAccessToken, quotationId, operationId, cancellationToken);
            }
            catch (HttpRequestException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
            catch (IOException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
            if (delegation is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return await ExecuteAsync<InvoiceCreationResult>(
            token => invoices.CreateAsync(quotationId, input, operationId, token, delegation), cancellationToken, ReadCreationReceipt);
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken,
        Func<byte[], T>? readResponse = null)
    {
        HttpResponseMessage response;
        try { response = await send(cancellationToken); }
        catch (HttpRequestException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes) return Results.StatusCode(StatusCodes.Status502BadGateway);
            try
            {
                var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
                var value = readResponse is null ? JsonSerializer.Deserialize<T>(bytes, Json) : readResponse(bytes);
                return value is null ? Results.StatusCode(StatusCodes.Status502BadGateway) : Results.Ok(value);
            }
            catch (JsonException) { return Results.StatusCode(StatusCodes.Status502BadGateway); }
        }
    }

    private static InvoiceCreationResult ReadCreationReceipt(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int? invoiceId = null, state = null, emailState = null;
        string? providerMessageId = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            string name;
            try { name = property.Name; }
            catch (InvalidOperationException) { throw new JsonException(); }
            var isInvoiceId = name.Equals(nameof(InvoiceCreationResult.InvoiceId), StringComparison.OrdinalIgnoreCase);
            var isState = name.Equals(nameof(InvoiceCreationResult.State), StringComparison.OrdinalIgnoreCase);
            var isEmailState = name.Equals(nameof(InvoiceCreationResult.EmailState), StringComparison.OrdinalIgnoreCase);
            var isProviderMessageId = name.Equals(nameof(InvoiceCreationResult.ProviderMessageId), StringComparison.OrdinalIgnoreCase);
            if (!isInvoiceId && !isState && !isEmailState && !isProviderMessageId) continue;
            if (!seen.Add(name)) throw new JsonException();
            if (isProviderMessageId)
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Value.ValueKind != JsonValueKind.String) throw new JsonException();
                try { providerMessageId = property.Value.GetString(); }
                catch (InvalidOperationException) { throw new JsonException(); }
            }
            else
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value)) throw new JsonException();
                if (isInvoiceId) invoiceId = value;
                else if (isState) state = value;
                else emailState = value;
            }
        }
        if (invoiceId is null or <= 0 || state is null || emailState is null) throw new JsonException();
        return new(invoiceId.Value, state.Value, emailState.Value, providerMessageId);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > MaximumResponseBytes) throw new JsonException("Accounting invoice creation response exceeded the safe limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
