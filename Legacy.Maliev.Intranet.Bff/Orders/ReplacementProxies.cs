using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Current employee transport. No service-token handler or automatic write retries.</summary>
public sealed class ReplacementProxy(HttpClient client, EmployeeAdministrationCredentialSender sender)
{
    private readonly HttpClient bounded = Bound(client);
    private static HttpClient Bound(HttpClient value) { value.MaxResponseContentBufferSize = 1048576; return value; }
    /// <summary>Rechecks session permissions and forwards the server-held employee credential once.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string permission, HttpContext context, CancellationToken c) =>
        sender.SendAsync(bounded, request, permission, context, c, LegacyEmployeePermissions.OrdersRead);
}
/// <summary>Private metadata transport; final evidence acceptance belongs to the owner receipt consumer.</summary>
public sealed class ReplacementEvidenceProxy(HttpClient client, EmployeeAdministrationCredentialSender sender)
{
    private readonly HttpClient bounded = Bound(client);
    private static HttpClient Bound(HttpClient value) { value.MaxResponseContentBufferSize = 1048576; return value; }
    /// <summary>Forwards the current employee credential for customer-scoped metadata only.</summary>
    public Task<HttpResponseMessage> ReadAsync(string path, HttpContext context, CancellationToken c) => sender.SendAsync(bounded, new(HttpMethod.Get, path), "legacy-file.documents.read", context, c, ReplacementPermissions.Read);
}
/// <summary>Order-bound replacement routes with independent acceptance and CSRF gates.</summary>
public static class ReplacementEndpoints
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.General);
    private static readonly HashSet<string> Decisions = ["Approve", "Reject", "AuthorizeRetry", "WaiveReturn", "WaiveQuantity"];
    private static readonly HashSet<string> Commands = ["StartAttempt", "CompleteQa", "RecordReturn", "Ship", "ConfirmDelivery", "Close", "RecordRecoveryFact"];
    /// <summary>Registers a dedicated transport without workload credential or resilience handlers.</summary>
    public static IServiceCollection AddReplacementBff(this IServiceCollection services, IConfiguration configuration)
    {
#pragma warning disable EXTEXP0001
        services.AddHttpClient<ReplacementProxy>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Order"] ?? throw new InvalidOperationException("Services:Order is required."));
            client.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
#pragma warning disable EXTEXP0001
        services.AddHttpClient<ReplacementEvidenceProxy>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:File"] ?? "https+http://legacy-maliev-file-service");
            client.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return services;
    }
    /// <summary>Maps protected order-scoped reads, commands, decisions and operation reconciliation.</summary>
    public static void MapReplacementBff(this WebApplication app)
    {
        var group = app.MapGroup("/bff/orders/{orderId:int}/replacements");
        // Independent acceptance gate: a flag cannot replace downstream authority/evidence.
        group.AddEndpointFilter(async (context, next) => app.Configuration.GetValue("ReplacementCases:Enabled", false) ? await next(context) : Results.StatusCode(503));
        group.MapGet("", List).RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapPost("", Report).AddEndpointFilter<AntiforgeryValidationFilter>().RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Write).RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapGet("/evidence", (int orderId, int? offset, ReplacementProxy proxy, ReplacementEvidenceProxy files, HttpContext context, CancellationToken c) => Metadata(orderId, null, offset ?? 0, proxy, files, context, c))
            .RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", "legacy-file.documents.read").RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapGet("/evidence/{documentId:guid}/versions", (int orderId, Guid documentId, int? offset, ReplacementProxy proxy, ReplacementEvidenceProxy files, HttpContext context, CancellationToken c) => Metadata(orderId, documentId, offset ?? 0, proxy, files, context, c))
            .RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", "legacy-file.documents.read").RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapGet("/operations/{operationId:guid}", Operation).RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapPost("/{caseId:int}/commands", (int orderId, int caseId, ReplacementCommandInput input, ReplacementProxy proxy, HttpContext context, CancellationToken c) => Command(orderId, caseId, input, false, proxy, context, c))
            .AddEndpointFilter<AntiforgeryValidationFilter>().RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", ReplacementPermissions.Write).RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
        group.MapPost("/{caseId:int}/decisions", (int orderId, int caseId, ReplacementCommandInput input, ReplacementProxy proxy, HttpContext context, CancellationToken c) => Command(orderId, caseId, input, true, proxy, context, c))
            .AddEndpointFilter<AntiforgeryValidationFilter>().RequireAuthorization(p => p.RequireClaim("permissions", ReplacementPermissions.Read).RequireClaim("permissions", ReplacementPermissions.Approve).RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead));
    }
    private static Task<IResult> Metadata(int orderId, Guid? documentId, int offset, ReplacementProxy proxy, ReplacementEvidenceProxy files, HttpContext context, CancellationToken c) => Bounded(async () =>
    {
        // Frozen local wire is not accepted producer availability. Leave this second gate off.
        if (!context.RequestServices.GetRequiredService<IConfiguration>().GetValue("ReplacementCases:EvidencePickerEnabled", false)) return Results.StatusCode(503);
        if (offset is < 0 or > 10000 || documentId == Guid.Empty) return Results.BadRequest();
        var (customer, failure) = await Customer(orderId, ReplacementPermissions.Read, proxy, context, c); if (failure is not null) return failure;
        var path = documentId is { } id ? $"/customers/{customer}/documents/{id:D}/versions?offset={offset}&limit=20" : $"/customers/{customer}/documents?offset={offset}&limit=20";
        using var response = await files.ReadAsync(path, context, c); if (!response.IsSuccessStatusCode) return Failure(response);
        if (documentId is { } selected)
        {
            var values = await Read<ReplacementDocumentVersionView[]>(response, c);
            if (values is null || values.Length > 20 || values.Any(x => x.DocumentId != selected || x.VersionId == Guid.Empty || x.VersionNumber <= 0 || x.Revision <= 0 || x.CreatedAtUtc.Offset != TimeSpan.Zero
                || x.ContentSha256 is not { Length: 64 } || x.ContentSha256.Any(ch => ch is not (>= 'a' and <= 'f' or >= '0' and <= '9')))) return Results.StatusCode(503);
            return Results.Json(values);
        }
        var documents = await Read<ReplacementDocumentView[]>(response, c);
        if (documents is null || documents.Length > 20 || documents.Any(x => x.CustomerId != customer || x.DocumentId == Guid.Empty || x.Revision <= 0 || x.Visibility is not ("Internal" or "Customer"))) return Results.StatusCode(503);
        return Results.Json(documents);
    }, c);
    private static async Task<IResult> List(int orderId, ReplacementProxy proxy, HttpContext context, CancellationToken c) => await Bounded(async () =>
    {
        var (customer, failure) = await Customer(orderId, ReplacementPermissions.Read, proxy, context, c); if (failure is not null) return failure;
        using var response = await proxy.SendAsync(new(HttpMethod.Get, $"/replacementcases/customers/{customer}/orders/{orderId}"), ReplacementPermissions.Read, context, c);
        if (!response.IsSuccessStatusCode) return Failure(response);
        var values = await Read<ReplacementStoredCaseView[]>(response, c);
        if (values is null || values.Length > 100 || values.Any(x => !Matches(x, customer, orderId))) return Results.StatusCode(503);
        return Results.Json(values);
    }, c);
    private static async Task<IResult> Report(int orderId, ReplacementReportInput input, ReplacementProxy proxy, HttpContext context, CancellationToken c) => await Bounded(async () =>
    {
        if (input.Quantity <= 0 || input.Reason is not ("CarrierDamage" or "ManufacturingNonconformance") || input.Evidence is null || input.Evidence.DocumentId == Guid.Empty || input.Evidence.VersionId == Guid.Empty || !Key(context, out var key)) return Results.BadRequest();
        var (customer, failure) = await Customer(orderId, ReplacementPermissions.Write, proxy, context, c); if (failure is not null) return failure;
        // Customer and original lineage come exclusively from the canonical protected Order read.
        var request = new HttpRequestMessage(HttpMethod.Post, $"/replacementcases/customers/{customer}") { Content = JsonContent.Create(new { CustomerId = customer, input.Reason, Affected = new[] { new { OrderId = orderId, Quantity = input.Quantity } }, input.Evidence }, options: Wire) };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await proxy.SendAsync(request, ReplacementPermissions.Write, context, c);
        return await Result(response, customer, orderId, c);
    }, c);
    private static async Task<IResult> Operation(int orderId, Guid operationId, ReplacementProxy proxy, HttpContext context, CancellationToken c) => await Bounded(async () =>
    {
        if (operationId == Guid.Empty) return Results.BadRequest();
        var (customer, failure) = await Customer(orderId, ReplacementPermissions.Read, proxy, context, c); if (failure is not null) return failure;
        using var response = await proxy.SendAsync(new(HttpMethod.Get, $"/replacementcases/customers/{customer}/operations/{operationId:D}"), ReplacementPermissions.Read, context, c);
        return await Result(response, customer, orderId, c);
    }, c);
    private static async Task<IResult> Command(int orderId, int caseId, ReplacementCommandInput input, bool decision, ReplacementProxy proxy, HttpContext context, CancellationToken c) => await Bounded(async () =>
    {
        if (caseId <= 0 || input.ExpectedRevision <= 0 || input.Command.ValueKind != JsonValueKind.Object || input.Command.GetRawText().Length > 16384 || !input.Command.TryGetProperty("Kind", out var kind) || kind.ValueKind != JsonValueKind.String || !Key(context, out var key)) return Results.BadRequest();
        var name = kind.GetString()!;
        var recoveryDecision = name == "RecordRecoveryFact" && input.Command.TryGetProperty("FactKind", out var factKind) && factKind.ValueKind == JsonValueKind.String && factKind.GetString() is "Approved" or "Received";
        if (decision ? !(Decisions.Contains(name) || recoveryDecision) : (!Commands.Contains(name) || recoveryDecision)) return Results.BadRequest();
        var permission = decision ? ReplacementPermissions.Approve : ReplacementPermissions.Write;
        var (customer, failure) = await Customer(orderId, permission, proxy, context, c); if (failure is not null) return failure;
        using var current = await proxy.SendAsync(new(HttpMethod.Get, $"/replacementcases/{caseId}"), ReplacementPermissions.Read, context, c);
        if (!current.IsSuccessStatusCode) return Failure(current);
        var existing = await Read<ReplacementStoredCaseView>(current, c);
        if (existing?.Id != caseId) return Results.StatusCode(503);
        if (!Matches(existing, customer, orderId)) return Results.StatusCode(403);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/replacementcases/{caseId}/{(decision ? "decisions" : "commands")}") { Content = JsonContent.Create(input, options: Wire) };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await proxy.SendAsync(request, permission, context, c);
        return await Result(response, customer, orderId, c, caseId, context);
    }, c);
    private static async Task<(int Customer, IResult? Failure)> Customer(int orderId, string permission, ReplacementProxy proxy, HttpContext context, CancellationToken c)
    {
        if (orderId <= 0) return (0, Results.BadRequest());
        using var response = await proxy.SendAsync(new(HttpMethod.Get, $"/Orders/{orderId}"), permission, context, c);
        if (!response.IsSuccessStatusCode) return (0, Failure(response));
        var value = await Read<OrderDetailItem>(response, c);
        return value is { CustomerId: > 0 } && value.Id == orderId ? (value.CustomerId.Value, null) : (0, Results.StatusCode(503));
    }
    private static bool Matches(ReplacementStoredCaseView? value, int customer, int orderId) => value is { Id: > 0, Value.CustomerId: > 0, Value.Revision: > 0 }
        && value.Value.CustomerId == customer && value.Value.Originals is { Count: > 0 and <= 100 }
        && value.Value.Originals.All(x => x.OrderId > 0 && x.CustomerId == customer)
        && value.Value.Originals.Select(x => x.OrderId).Distinct().Count() == value.Value.Originals.Count
        && value.Value.Originals.Any(x => x.OrderId == orderId)
        && value.Value.Attempts is not null && value.Value.Shipments is not null && value.Value.Audit is not null && value.Value.Returns is not null && value.Value.Waivers is not null && value.Value.RecoveryFacts is not null;
    private static async Task<IResult> Result(HttpResponseMessage response, int customer, int orderId, CancellationToken c, int? expectedCaseId = null, HttpContext? context = null)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (expectedCaseId is not null && context is not null && response.StatusCode == HttpStatusCode.Conflict
                && response.Headers.TryGetValues("X-Replacement-Rejection", out var rejection) && rejection.SingleOrDefault() == "Revision")
                context.Response.Headers["X-Replacement-Rejection"] = "Revision";
            return Failure(response);
        }
        var value = await Read<ReplacementStoredCaseView>(response, c);
        return (expectedCaseId is null || value?.Id == expectedCaseId) && Matches(value, customer, orderId) ? Results.Json(value, statusCode: (int)response.StatusCode) : Results.StatusCode(503);
    }
    private static IResult Failure(HttpResponseMessage response) => Results.StatusCode(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests ? (int)response.StatusCode : 503);
    private static bool Key(HttpContext context, out string key)
    {
        key = context.Request.Headers["Idempotency-Key"].ToString(); return Guid.TryParseExact(key, "D", out var id) && id != Guid.Empty;
    }
    private static async Task<T?> Read<T>(HttpResponseMessage response, CancellationToken c)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(c); using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, c)) > 0) { if (bytes.Length + count > 1048576) throw new IOException("Bounded replacement response exceeded."); await bytes.WriteAsync(buffer.AsMemory(0, count), c); }
        return JsonSerializer.Deserialize<T>(bytes.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    private static async Task<IResult> Bounded(Func<Task<IResult>> action, CancellationToken c)
    {
        try { return await action(); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException || e is OperationCanceledException && !c.IsCancellationRequested) { return Results.StatusCode(503); }
    }
}
