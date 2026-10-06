using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;

namespace Legacy.Maliev.Intranet.Bff.Catalog;

/// <summary>Independent registration hooks; no changes to existing editors or persistence routes.</summary>
public static class LookupEndpointExtensions
{
    /// <summary>Exact employee permission for administrative address lookup.</summary>
    public const string LocationsRead = "legacy-catalog.locations.read";
    /// <summary>Exact employee permission for company suggestions.</summary>
    public const string CompaniesRead = "legacy-catalog.companies.read";
    private const string Limiter = "catalog-lookup";

    /// <summary>Registers the bounded authenticated Catalog client and lookup rate limiter.</summary>
    public static IServiceCollection AddCatalogLookups(this IServiceCollection services, IConfiguration configuration)
    {
#pragma warning disable EXTEXP0001 // Use the existing explicit no-retry BFF boundary API.
        services.AddHttpClient<LookupCatalogProxy>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Catalog"]
                ?? throw new InvalidOperationException("Services:Catalog is required."));
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 256 * 1024;
        }).RemoveAllLoggers().RemoveAllResilienceHandlers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
#pragma warning restore EXTEXP0001
        services.AddRateLimiter(options => options.AddFixedWindowLimiter(Limiter, limiter =>
        {
            limiter.PermitLimit = 120;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
            limiter.AutoReplenishment = true;
        }));
        return services;
    }

    /// <summary>Maps same-origin employee lookups with permissions and parsing CSRF protection.</summary>
    public static IEndpointRouteBuilder MapCatalogLookups(this IEndpointRouteBuilder app)
    {
        var addresses = app.MapGroup("/bff/lookups/thai-addresses")
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", LocationsRead))
            .RequireRateLimiting(Limiter);
        foreach (var level in new[] { "provinces", "districts", "subdistricts" })
        {
            var resource = level;
            addresses.MapGet("/" + resource, (HttpContext context, LookupCatalogProxy proxy, CancellationToken ct) =>
                GetAddressAsync<LookupPage<LookupArea>>(resource, context, proxy, ct));
        }
        addresses.MapGet("/postcodes", (HttpContext context, LookupCatalogProxy proxy, CancellationToken ct) =>
            GetAddressAsync<LookupPage<string>>("postcodes", context, proxy, ct));
        addresses.MapGet("/autocomplete", (HttpContext context, LookupCatalogProxy proxy, CancellationToken ct) =>
            GetAddressAsync<LookupAddressPage>("autocomplete", context, proxy, ct));
        addresses.MapPost("/resolve", ResolveAsync).AddEndpointFilter<AntiforgeryValidationFilter>();
        app.MapGet("/bff/lookups/companies/search", SearchCompanyAsync)
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireClaim("permissions", CompaniesRead))
            .RequireRateLimiting(Limiter);
        return app;
    }

    private static Task<IResult> GetAddressAsync<T>(string resource, HttpContext context, LookupCatalogProxy proxy, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = AddressQuery(context.Request);
        return query is null ? Task.FromResult<IResult>(Results.BadRequest()) :
            ForwardAsync<T>(() => proxy.GetAsync("thai-addresses/" + resource, query, ct), context, ct);
    }

    private static async Task<IResult> ResolveAsync(HttpContext context, LookupCatalogProxy proxy, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            // Bound actual bytes too, including chunked requests, before deserialization.
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > 16 * 1024) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
            }
            var input = JsonSerializer.Deserialize<LookupResolveRequest>(buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (input is null || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 2048 || !ValidConstraints(input.Constraints))
                return Results.BadRequest();
            return await ForwardAsync<LookupResolveResponse>(() => proxy.ResolveAsync(input, ct), context, ct);
        }
        catch (JsonException) { return Results.BadRequest(); }
    }

    private static Task<IResult> SearchCompanyAsync(HttpContext context, LookupCatalogProxy proxy, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var input = context.Request.Query;
        var q = input["q"].ToString().Trim();
        var type = input["queryType"].ToString();
        var language = input["language"].ToString();
        var limit = input["limit"].ToString();
        if (type == "tax-id") q = NormalizeDigits(q);
        if (type is not ("name" or "tax-id") || language is not ("th" or "en") ||
            q.Length is < 2 or > 128 || type == "tax-id" && !Digits(q, 13) || !ValidLimit(limit))
            return Task.FromResult<IResult>(Results.BadRequest());
        return ForwardAsync<LookupCompanyPage>(() => proxy.GetAsync("companies/search", new Dictionary<string, string?>
            { ["q"] = q, ["queryType"] = type, ["language"] = language, ["limit"] = limit }, ct), context, ct);
    }

    private static Dictionary<string, string?>? AddressQuery(HttpRequest request)
    {
        var query = new Dictionary<string, string?>();
        foreach (var name in new[] { "q", "provinceCode", "districtCode", "subdistrictCode", "postcode", "cursor", "limit" })
        {
            if (request.Query[name].Count > 1) return null;
            var value = request.Query[name].ToString();
            query[name] = string.IsNullOrEmpty(value) ? null : value;
        }
        query["limit"] ??= "20";
        if (query["postcode"] is { } postcode) query["postcode"] = NormalizeDigits(postcode);
        return query["q"]?.Length > 128 || query["cursor"]?.Length > 512 || !ValidLimit(query["limit"]!) ||
            !ValidConstraints(new(query["provinceCode"], query["districtCode"], query["subdistrictCode"], query["postcode"])) ? null : query;
    }

    private static bool ValidConstraints(LookupAddressConstraints? input) => input is null ||
        (input.ProvinceCode is null || Digits(input.ProvinceCode, 2)) &&
        (input.DistrictCode is null || Digits(input.DistrictCode, 4)) &&
        (input.SubdistrictCode is null || Digits(input.SubdistrictCode, 6)) &&
        (input.Postcode is null || Digits(NormalizeDigits(input.Postcode), 5));
    private static bool ValidLimit(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit is >= 1 and <= 50;
    private static bool Digits(string value, int length) => value.Length == length && value.All(char.IsAsciiDigit);
    private static string NormalizeDigits(string value) => new(value.Select(c => c is >= '๐' and <= '๙' ? (char)('0' + c - '๐') : c).ToArray());

    private static async Task<IResult> ForwardAsync<T>(Func<Task<HttpResponseMessage>> send, HttpContext context, CancellationToken ct)
    {
        try
        {
            using var response = await send();
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (status == 429 && response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1))
                    context.Response.Headers.RetryAfter = Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return Results.Problem(statusCode: status is 400 or 401 or 403 or 404 or 409 or 422 or 429 or 503 ? status : 502,
                    title: "Lookup unavailable");
            }
            var value = await response.Content.ReadFromJsonAsync<T>(ct);
            return !LookupResponseValidation.IsValid(value) ? Results.Problem(statusCode: 502, title: "Invalid lookup response") : Results.Ok(value);
        }
        catch (JsonException) { return Results.Problem(statusCode: 502, title: "Invalid lookup response"); }
        catch (HttpRequestException) { return Results.Problem(statusCode: 503, title: "Lookup unavailable"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem(statusCode: 503, title: "Lookup unavailable"); }
    }
}
