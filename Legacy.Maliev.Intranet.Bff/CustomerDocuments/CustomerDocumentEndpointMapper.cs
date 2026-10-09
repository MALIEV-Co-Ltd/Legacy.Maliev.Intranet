using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Antiforgery;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Bff.CustomerDocuments;

/// <summary>Same-origin employee endpoints. FileService enforces current resource-scoped authority.</summary>
public static class CustomerDocumentEndpointMapper
{
    /// <summary>Maps additive routes only; registration is separately owner integrated.</summary>
    public static IEndpointRouteBuilder MapCustomerDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/customers/{customerId:int}/documents").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapGet("/{documentId:guid}/versions", VersionsAsync);
        group.MapGet("/{documentId:guid}/nda", NdaAsync);
        group.MapGet("/{documentId:guid}/versions/{versionId:guid}/receipt", ReceiptAsync);
        group.MapGet("/{documentId:guid}/versions/{versionId:guid}/download", DownloadAsync);
        group.MapPost("", UploadAsync).AddEndpointFilter<DocumentMutationFilter>();
        group.MapPost("/{documentId:guid}/versions", ReplaceAsync).AddEndpointFilter<DocumentMutationFilter>();
        group.MapPost("/{documentId:guid}/archive", ArchiveAsync).AddEndpointFilter<JsonMutationFilter>();
        group.MapPost("/{documentId:guid}/nda/verification", VerifyAsync).AddEndpointFilter<JsonMutationFilter>();
        group.MapPost("/{documentId:guid}/versions/{versionId:guid}/verification", VerifyEvidenceAsync).AddEndpointFilter<JsonMutationFilter>();
        endpoints.MapGet("/bff/staff/nda-reminders", RemindersAsync).RequireAuthorization();
        return endpoints;
    }
    private static async Task<IResult> VersionsAsync(int customerId, Guid documentId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0 || documentId == Guid.Empty) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.VersionsAsync(customerId, documentId, credential, token);
        return result.StatusCode == 200 && result.Value is { Count: <= 50 } && result.Value.All(x => x.DocumentId == documentId && x.VersionId != Guid.Empty && x.VersionNumber > 0 && x.Revision > 0 && KnownKind(x.Kind) && Digest(x.ContentSha256) && KnownVerification(x.VerificationStatus) && x.CreatedAtUtc != default && x.CreatedAtUtc.Offset == TimeSpan.Zero && ValidVerificationEvidence(x.VerificationStatus, x.VerifiedBySubject, x.VerifiedAtUtc)) ? Results.Ok(result.Value) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }
    private static async Task<IResult> NdaAsync(int customerId, Guid documentId, Guid versionId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0 || documentId == Guid.Empty || versionId == Guid.Empty) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.NdaAsync(customerId, documentId, versionId, credential, token);
        return result.StatusCode == 200 && result.Value is { } value && ValidNda(value, documentId, versionId) ? Results.Ok(value) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }

    private static async Task<string?> CredentialAsync(HttpContext context, EmployeeSessionService sessions, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.User.Identity?.IsAuthenticated != true || context.User.FindFirst("identity_kind")?.Value != "employee") return null;
        return await sessions.GetAccessTokenAsync(context, token);
    }
    private static async Task<IResult> ListAsync(int customerId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.ListAsync(customerId, credential, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode);
        return result.Value.Count > 50 || result.Value.Any(x => x.CustomerId != customerId || x.DocumentId == Guid.Empty || x.Revision <= 0 || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 250 || x.Visibility is not ("Internal" or "Customer") || !KnownKind(x.Kind)) ? Results.StatusCode(503) : Results.Ok(result.Value);
    }
    private static async Task<IResult> ReceiptAsync(int customerId, Guid documentId, Guid versionId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0 || documentId == Guid.Empty || versionId == Guid.Empty) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.ReceiptAsync(customerId, documentId, versionId, credential, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
        return result.Value.CustomerId != customerId || result.Value.DocumentId != documentId || result.Value.VersionId != versionId || result.Value.Revision <= 0 || !Digest(result.Value.ContentSha256) || !KnownKind(result.Value.Kind) || !KnownVerification(result.Value.VerificationStatus) || !ValidVerificationEvidence(result.Value.VerificationStatus, result.Value.VerifiedBySubject, result.Value.VerifiedAtUtc) ? Results.StatusCode(503) : Results.Ok(result.Value);
    }
    private static async Task<IResult> DownloadAsync(int customerId, Guid documentId, Guid versionId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0 || documentId == Guid.Empty || versionId == Guid.Empty) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.DownloadAsync(customerId, documentId, versionId, credential, token);
        if (result.StatusCode != 200 || result.Value is null) return Results.StatusCode(result.StatusCode);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(result.Value, "application/octet-stream", $"document-{documentId:D}-{versionId:D}", enableRangeProcessing: false);
    }
    private static Task<IResult> UploadAsync(int customerId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token) => UploadCoreAsync(customerId, null, context, sessions, proxy, token);
    private static Task<IResult> ReplaceAsync(int customerId, Guid documentId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token) => documentId == Guid.Empty ? Task.FromResult<IResult>(Results.BadRequest()) : UploadCoreAsync(customerId, documentId, context, sessions, proxy, token);
    private static async Task<IResult> ArchiveAsync(int customerId, Guid documentId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        var body = await ReadBoundedJsonAsync<CustomerDocumentArchiveRequest>(context, token);
        if (body.StatusCode != 200 || body.Value is null) return Results.StatusCode(body.StatusCode);
        var input = body.Value;
        if (customerId <= 0 || documentId == Guid.Empty || input.ExpectedRevision <= 0 || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        return Results.StatusCode(await proxy.ArchiveAsync(customerId, documentId, input, credential, token));
    }
    private static async Task<IResult> UploadCoreAsync(int customerId, Guid? documentId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        if (customerId <= 0) return Results.BadRequest();
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 20 * 1024 * 1024 + 64 * 1024;
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        if (!context.Request.HasFormContentType || !Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key)) return Results.BadRequest();
        IFormCollection form;
        try { form = await context.Request.ReadFormAsync(token); } catch (InvalidDataException) { return Results.StatusCode(413); }
        if (form.Files.Count != 1 || form.Files[0].Length is <= 0 or > 20 * 1024 * 1024) return Results.StatusCode(413);
        if (form.Files[0].ContentType is not ("application/pdf" or "image/png" or "image/jpeg")) return Results.StatusCode(415);
        if (!CommercialAssociations(form)) return Results.BadRequest();
        var result = await proxy.UploadAsync(customerId, documentId, form, key.ToString("D"), credential, token);
        return result.StatusCode == 200 && result.Value is { } value && value.CustomerId == customerId && value.DocumentId != Guid.Empty && (documentId is null || value.DocumentId == documentId) && value.VersionId != Guid.Empty && value.VersionNumber > 0 && value.Revision > 0 && Digest(value.ContentSha256) ? Results.Ok(value) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }
    private static bool CommercialAssociations(IFormCollection form)
    {
        if (!form.TryGetValue("Associations", out var values)) return true;
        if (values.Count != 1 || values[0] is not { Length: <= 16384 } json) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 100) return false;
            var seen = new HashSet<(string Kind, int ResourceId)>(); var quotations = 0;
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 || !item.TryGetProperty("Kind", out var kind) || kind.ValueKind != JsonValueKind.String || !item.TryGetProperty("ResourceId", out var resource) || resource.ValueKind != JsonValueKind.Number || !resource.TryGetInt32(out var id) || id <= 0) return false;
                var name = kind.GetString();
                if (name is not ("Order" or "Quotation" or "Replacement") || !seen.Add((name, id)) || name == "Quotation" && ++quotations > 1) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }
    private static async Task<IResult> VerifyAsync(int customerId, Guid documentId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        var body = await ReadBoundedJsonAsync<CustomerNdaVerificationRequest>(context, token);
        if (body.StatusCode != 200 || body.Value is null) return Results.StatusCode(body.StatusCode);
        var input = body.Value;
        if (customerId <= 0 || documentId == Guid.Empty || input.VersionId == Guid.Empty || input.ExpectedRevision <= 0) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.VerifyAsync(customerId, documentId, input, credential, token);
        return result.StatusCode == 200 && result.Value is not null && result.Value.DocumentId == documentId && result.Value.VersionId == input.VersionId ? Results.Ok(result.Value) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }
    private static async Task<IResult> VerifyEvidenceAsync(int customerId, Guid documentId, Guid versionId, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        var body = await ReadBoundedJsonAsync<CustomerDocumentVerificationRequest>(context, token);
        if (body.StatusCode != 200 || body.Value is null) return Results.StatusCode(body.StatusCode);
        var input = body.Value;
        if (customerId <= 0 || documentId == Guid.Empty || versionId == Guid.Empty || input.ExpectedVerificationRevision <= 0 || input.Status is not ("Verified" or "Rejected") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1024) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.VerifyEvidenceAsync(customerId, documentId, versionId, input, credential, token);
        return result.StatusCode == 200 && result.Value is { } value && value.CustomerId == customerId && value.DocumentId == documentId && value.VersionId == versionId && value.Revision > input.ExpectedVerificationRevision && KnownKind(value.Kind) && value.Kind != "Nda" && Digest(value.ContentSha256) && value.VerificationStatus == input.Status && ValidVerificationEvidence(value.VerificationStatus, value.VerifiedBySubject, value.VerifiedAtUtc) ? Results.Ok(value) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }
    private static async Task<IResult> RemindersAsync(DateTimeOffset? dueFromUtc, DateTimeOffset? dueThroughUtc, string? state, int? limit, HttpContext context, EmployeeSessionService sessions, CustomerDocumentProxy proxy, CancellationToken token)
    {
        var bound = limit ?? 20;
        var selectedState = state?.ToLowerInvariant() switch { "due" => "Due", "missed" => "Missed", "cancelled" => "Cancelled", _ => null };
        if (bound is < 1 or > 100 || !Utc(dueFromUtc) || !Utc(dueThroughUtc) || state is not null && selectedState is null || dueFromUtc is not null && dueThroughUtc is not null && (dueThroughUtc.Value < dueFromUtc.Value || dueThroughUtc.Value - dueFromUtc.Value > TimeSpan.FromDays(366))) return Results.BadRequest();
        var credential = await CredentialAsync(context, sessions, token); if (credential is null) return Results.Unauthorized();
        var result = await proxy.RemindersAsync(credential, token, dueFromUtc, dueThroughUtc, selectedState, bound);
        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return result.StatusCode == 200 && result.Value is { } values && values.Count <= bound && !string.IsNullOrWhiteSpace(subject) && values.All(x => x.Id != Guid.Empty && x.NdaId != Guid.Empty && x.VersionId != Guid.Empty && x.RenewalRevision > 0 && x.LeadDays is >= 0 and <= 3660 && x.ResponsibleEmployeeSubject == subject && x.DueAtUtc != default && x.DueAtUtc.Offset == TimeSpan.Zero && (x.State is "Due" or "Missed" or "Cancelled") && (selectedState is null || x.State == selectedState) && (dueFromUtc is null || x.DueAtUtc >= dueFromUtc.Value) && (dueThroughUtc is null || x.DueAtUtc <= dueThroughUtc.Value)) ? Results.Ok(values) : Results.StatusCode(result.StatusCode == 200 ? 503 : result.StatusCode);
    }
    private static readonly JsonSerializerOptions BrowserJson = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private static async Task<CustomerDocumentResult<T>> ReadBoundedJsonAsync<T>(HttpContext context, CancellationToken token) where T : class
    {
        const int maximum = 64 * 1024;
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maximum;
        if (!context.Request.HasJsonContentType()) return new(415, null);
        if (context.Request.ContentLength > maximum) return new(413, null);
        using var body = new MemoryStream(); var buffer = new byte[4096];
        try
        {
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, token)) != 0)
            {
                if (body.Length + count > maximum) return new(413, null);
                await body.WriteAsync(buffer.AsMemory(0, count), token);
            }
            body.Position = 0;
            var value = await JsonSerializer.DeserializeAsync<T>(body, BrowserJson, token);
            return new(value is null ? 400 : 200, value);
        }
        catch (JsonException) { return new(400, null); }
        catch (BadHttpRequestException exception) { return new(exception.StatusCode == 413 ? 413 : 400, null); }
        catch (IOException) { return new(400, null); }
    }
    private class DocumentMutationFilter(IAntiforgery antiforgery) : IEndpointFilter
    {
        protected virtual long Maximum => 20 * 1024 * 1024 + 64 * 1024;
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var limit = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = Maximum;
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (InvalidDataException) { return Results.StatusCode(413); }
            return await next(context);
        }
    }
    private sealed class JsonMutationFilter(IAntiforgery antiforgery) : DocumentMutationFilter(antiforgery)
    {
        protected override long Maximum => 64 * 1024;
    }
    private static bool KnownKind(string? kind) => kind is "Nda" or "Corporate" or "BillingInstruction" or "Shipment" or "Release" or "Acceptance" or "Evidence";
    private static bool KnownVerification(string? status) => status is "PendingVerification" or "Verified" or "Rejected";
    private static bool Digest(string? digest) => digest is { Length: 64 } && digest.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidNda(CustomerNdaAgreementSummary value, Guid documentId, Guid versionId) =>
        value.DocumentId == documentId && value.VersionId == versionId && value.NdaId != Guid.Empty && value.VerificationRevision > 0 &&
        value.EffectiveAtUtc != default && value.EffectiveAtUtc.Offset == TimeSpan.Zero && Utc(value.ExpiresAtUtc) && Utc(value.RenewalAtUtc) && Utc(value.SurvivalEndsAtUtc) &&
        (value.AgreementStatus is "Future" or "Active" or "Expired" or "Superseded") && (value.ObligationStatus is "Protected" or "ReviewRequired") &&
        (value.SurvivalKind is "Unknown" or "Finite" or "Indefinite") && !string.IsNullOrWhiteSpace(value.ResponsibleEmployeeSubject);
    private static bool ValidVerificationEvidence(string status, string? subject, DateTimeOffset? verifiedAt) =>
        Utc(verifiedAt) && (status is not ("Verified" or "Rejected") ||
            !string.IsNullOrWhiteSpace(subject) && verifiedAt is { } time && time != default);
    private static bool Utc(DateTimeOffset? value) => value is null || value.Value.Offset == TimeSpan.Zero;
}
