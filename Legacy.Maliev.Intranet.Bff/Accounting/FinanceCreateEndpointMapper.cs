using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Accounting;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

internal static class FinanceCreateEndpointMapper
{
    public static async Task<IResult> GetAsync(FinancesProxy finances, OrderEmployeeReferenceProxy employees, OrderCatalogReferenceProxy catalog, CancellationToken token)
    {
        try
        {
            var directionsTask = finances.GetLookupAsync("directions", token);
            var typesTask = finances.GetLookupAsync("types", token);
            var methodsTask = finances.GetLookupAsync("methods", token);
            var employeesTask = employees.GetEmployeesAsync(token);
            var currenciesTask = catalog.GetCurrenciesAsync(token);
            await Task.WhenAll(directionsTask, typesTask, methodsTask, employeesTask, currenciesTask);
            using var directions = await directionsTask; using var types = await typesTask; using var methods = await methodsTask;
            directions.EnsureSuccessStatusCode(); types.EnsureSuccessStatusCode(); methods.EnsureSuccessStatusCode();
            return Results.Ok(new FinanceCreatePage(
                (await employeesTask).Select(x => new FinanceLookupItem(x.Id, x.Name)).ToArray(),
                await directions.Content.ReadFromJsonAsync<List<FinanceLookupItem>>(token) ?? [],
                await types.Content.ReadFromJsonAsync<List<FinanceLookupItem>>(token) ?? [],
                await methods.Content.ReadFromJsonAsync<List<FinanceLookupItem>>(token) ?? [],
                (await currenciesTask).Select(x => new CatalogCurrency(x.Id, x.ShortName)).ToArray()));
        }
        catch (Exception ex) when (Bounded(ex, token)) { return Unavailable(); }
    }

    public static async Task<IResult> CreateAsync(HttpContext context, FinancesProxy finances, FinanceFileProxy files, FinanceFileWorkflow workflow, ILogger<FinanceFileWorkflow> logger, CancellationToken token)
    {
        if (!context.Request.HasFormContentType || !Guid.TryParse(context.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var workflowId)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(token);
        FinancePaymentCreateRequest? input;
        try { input = JsonSerializer.Deserialize<FinancePaymentCreateRequest>(form["payment"].FirstOrDefault() ?? "", new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return Results.BadRequest(); }
        if (input is null || input.EmployeeId <= 0 || input.PaymentDirectionId <= 0 || input.PaymentTypeId <= 0 || input.PaymentMethodId <= 0 || input.CurrencyId <= 0 || input.Amount < 0 || string.IsNullOrWhiteSpace(input.Description)) return Results.BadRequest();
        var uploads = form.Files.Where(x => x.Length > 0).ToArray();
        if (uploads.Sum(x => x.Length) > 200L * 1024 * 1024) return Results.BadRequest();
        int? paymentId = null;
        try
        {
            using var created = await finances.CreateAsync(input, workflowId.ToString("D"), token);
            if (created.StatusCode == HttpStatusCode.Conflict) return Results.Conflict();
            if ((int)created.StatusCode >= 500) return Unavailable();
            created.EnsureSuccessStatusCode();
            paymentId = (await created.Content.ReadFromJsonAsync<FinancePaymentCreatedResult>(token))?.Id;
            if (paymentId is not > 0) throw new InvalidDataException("AccountingService returned an invalid payment.");
            if (uploads.Length > 0)
            {
                await workflow.UploadAsync(paymentId.Value, uploads,
                    async (selected, ct) => { using var r = await files.UploadAsync(paymentId.Value, selected, workflowId.ToString("D"), ct); if ((int)r.StatusCode >= 500) throw new FinanceCreateOutcomeUnknownException(); r.EnsureSuccessStatusCode(); var v = await r.Content.ReadFromJsonAsync<FinanceUploadResult>(ct) ?? throw new InvalidDataException(); return v.Object.Select(x => new FinanceStoredFile(x.Bucket, x.ObjectName)).ToArray(); },
                    async (id, stored, ct) => { using var r = await finances.CreateFileAsync(id, stored.Bucket, stored.ObjectName, Operation(workflowId, stored.ObjectName), ct); if ((int)r.StatusCode >= 500) throw new FinanceCreateOutcomeUnknownException(); r.EnsureSuccessStatusCode(); return await r.Content.ReadFromJsonAsync<FinanceFileItem>(ct) ?? throw new InvalidDataException(); },
                    async (item, ct) => { using var r = await finances.DeleteFileAsync(item.Id, ct); if (r.StatusCode != HttpStatusCode.NotFound) r.EnsureSuccessStatusCode(); },
                    async (stored, ct) => { using var r = await files.DeleteAsync(stored.Bucket, stored.ObjectName, ct); if (r.StatusCode != HttpStatusCode.NotFound) r.EnsureSuccessStatusCode(); },
                    token,
                    ex => ex is not FinanceCreateOutcomeUnknownException);
            }
            return Results.Ok(new FinancePaymentCreatedResult(paymentId.Value));
        }
        catch (FinanceCreateOutcomeUnknownException ex) { logger.LogWarning(ex, "Finance creation outcome is unresolved for workflow {WorkflowId}.", workflowId); return Unavailable(); }
        catch (Exception ex) when (Bounded(ex, token))
        {
            if (paymentId is not null) { using var cleanup = await finances.DeleteAsync(paymentId.Value, CancellationToken.None); }
            return Unavailable();
        }
    }

    private static string Operation(Guid id, string step) { var h = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{id:D}:{step}")); return new Guid(h.AsSpan(0, 16)).ToString("D"); }
    private static bool Bounded(Exception ex, CancellationToken token) => ex is HttpRequestException or InvalidDataException or JsonException or Polly.Timeout.TimeoutRejectedException || ex is OperationCanceledException && !token.IsCancellationRequested;
    private static IResult Unavailable() => Results.Problem(statusCode: 503, title: "Finance workflow unavailable");
    private sealed class FinanceCreateOutcomeUnknownException : Exception;
}
