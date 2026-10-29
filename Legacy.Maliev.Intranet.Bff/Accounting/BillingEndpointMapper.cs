using System.Net;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Employee-only same-origin billing reads and CSRF-protected financial previews.</summary>
public static class BillingEndpointMapper
{
    private static readonly JsonSerializerOptions ProducerJson = new() { MaxDepth = 32 };
    private const int MaximumBytes = 1024 * 1024;

    /// <summary>Maps default-disabled routes without granting issuance authority.</summary>
    public static void MapBillingEndpoints(this WebApplication app)
    {
        app.MapGet("/bff/customers/{customerId:int}/billing/accounts/{accountId:guid}",
            (int customerId, Guid accountId, HttpContext context, BillingProxy proxy, CancellationToken token) =>
                MapReadAsync(customerId, accountId, context, proxy, token))
            .RequireAuthorization(LegacyEmployeePermissions.AccountingRead);
        app.MapPost("/bff/customers/{customerId:int}/billing/accounts/{accountId:guid}/preview",
            (int customerId, Guid accountId, BillingPreviewRequest input, HttpContext context, BillingProxy proxy, CancellationToken token) =>
                MapPreviewAsync(customerId, accountId, input, context, proxy, token))
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireAuthorization(LegacyEmployeePermissions.AccountingRead);
    }

    /// <summary>Validates the producer customer/account projection before browser exposure.</summary>
    public static Task<IResult> MapReadAsync(int customerId, Guid accountId, HttpContext context, BillingProxy proxy, CancellationToken token) =>
        MapAsync<BillingAccountView>(customerId, accountId, context,
            deadline => proxy.ReadAsync(customerId, accountId, context, deadline),
            account => Valid(account, customerId, accountId), token);

    /// <summary>Preserves the requested account/revision; a preview never satisfies evidence requirements.</summary>
    public static Task<IResult> MapPreviewAsync(int customerId, Guid accountId, BillingPreviewRequest input,
        HttpContext context, BillingProxy proxy, CancellationToken token) =>
        MapAsync<BillingStagePreview>(customerId, accountId, context,
            deadline => proxy.PreviewAsync(customerId, accountId, input, context, deadline),
            preview => preview.AccountId == accountId && preview.Revision == input.ExpectedRevision && Valid(preview.Amount)
                && preview.Portions is not null && preview.Portions.Count > 0
                && Valid(preview.Portions, preview.Amount.Currency)
                && preview.Portions.Sum(line => line.Amount.Base) == preview.Amount.Base
                && preview.Portions.Sum(line => line.Amount.Vat) == preview.Amount.Vat
                && preview.Portions.Sum(line => line.Amount.Gross) == preview.Amount.Gross, token);

    private static async Task<IResult> MapAsync<T>(int customerId, Guid accountId, HttpContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> send, Func<T, bool> valid, CancellationToken token) where T : class
    {
        context.Response.Headers.CacheControl = "no-store";
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        if (!bool.TryParse(configuration["Billing:ReadEnabled"], out var enabled) || !enabled) return Results.StatusCode(503);
        if (customerId <= 0 || accountId == Guid.Empty) return Results.BadRequest();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await send(deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var status = (int)response.StatusCode;
                return Results.StatusCode(status is 400 or 401 or 403 or 404 or 409 or 429 or 503 ? status : 502);
            }
            if (response.Content.Headers.ContentLength > MaximumBytes) return Results.StatusCode(502);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, deadline.Token)) != 0)
            {
                if (buffer.Length + count > MaximumBytes) return Results.StatusCode(502);
                buffer.Write(chunk, 0, count);
            }
            var value = JsonSerializer.Deserialize<T>(buffer.ToArray(), ProducerJson);
            return value is not null && valid(value) ? Results.Ok(value) : Results.StatusCode(502);
        }
        catch (JsonException) { return Results.StatusCode(502); }
        catch (OverflowException) { return Results.StatusCode(502); }
        catch (HttpRequestException) { return Results.StatusCode(503); }
        catch (IOException) { return Results.StatusCode(503); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Results.StatusCode(503); }
    }

    private static bool Valid(BillingMoney money) => money is not null && !string.IsNullOrWhiteSpace(money.Currency)
        && money.Base >= 0 && money.Vat >= 0 && money.Base + money.Vat == money.Gross;

    private static bool Valid(IReadOnlyList<BillingLine> lines, string currency) => lines is not null
        && lines.All(line => line is not null && line.SourceLineId > 0 && !string.IsNullOrWhiteSpace(line.TaxCategory)
            && Valid(line.Amount) && line.Amount.Currency == currency)
        && lines.Select(line => line.SourceLineId).Distinct().Count() == lines.Count;

    private static bool Valid(BillingAccountView account, int customerId, Guid accountId) =>
        account.Id == accountId && account.Revision > 0 && account.Snapshot is { } source && source.CustomerId == customerId
        && source.QuotationId > 0 && !string.IsNullOrWhiteSpace(source.TaxId) && Valid(source.Cap) && Valid(account.Billed)
        && source.CurrencyPrecision is >= 0 and <= 4
        && Valid(source.Lines, source.Cap.Currency) && source.Lines.Count > 0
        && account.Billed.Currency == source.Cap.Currency && account.Billed.Gross <= source.Cap.Gross
        && account.Cash >= 0 && account.Withholding >= 0 && account.Outstanding >= 0 && account.Unbilled >= 0 && account.VatRecognized >= 0
        && account.Outstanding == account.Billed.Gross - account.Cash - account.Withholding
        && account.Unbilled == source.Cap.Gross - account.Billed.Gross && account.Stages is not null
        && account.Stages.All(stage => stage is not null)
        && account.Stages.Select(stage => stage.Id).Distinct().Count() == account.Stages.Count
        && account.Stages.All(stage => stage.Id != Guid.Empty && Enum.IsDefined(stage.Kind) && Valid(stage.Amount)
            && stage.Amount.Currency == source.Cap.Currency && stage.Cash >= 0 && stage.Withholding >= 0 && stage.Credit >= 0
            && stage.Recipient is not null && stage.Recipient.CustomerId == customerId && stage.Recipient.TaxId == source.TaxId
            && Valid(stage.Portions, source.Cap.Currency) && Valid(stage.Credits, source.Cap.Currency)
            && stage.Requirements is not null && stage.Requirements.All(requirement => requirement is not null
                && Enum.IsDefined(requirement.Kind) && requirement.OrderIds is not null
                && requirement.OrderIds.All(id => id > 0) && requirement.OrderIds.Distinct().Count() == requirement.OrderIds.Count));
}
