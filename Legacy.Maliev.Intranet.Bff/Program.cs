using System.Security.Claims;
using Legacy.Maliev.Intranet.Bff;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Bff.Catalog;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.AddServiceDefaults();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddLegacyIntranetDataProtection();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddLegacyAccessTokenValidation(
    builder.Configuration,
    validateOnStart: !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddSingleton<DistributedTicketStore>();
builder.Services.AddScoped<EmployeeSessionService>();
builder.Services.AddOptions<LegacyEmployeeCompatibilityOptions>()
    .Bind(builder.Configuration.GetSection(LegacyEmployeeCompatibilityOptions.SectionName));
builder.Services.AddHttpClient<CatalogMaterialsProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyAuthClient, LegacyAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("employee-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Legacy.Maliev.Intranet.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Legacy.Maliev.Intranet.Bff";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/bff"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/bff"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<DistributedTicketStore>((options, store) => options.SessionStore = store);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("legacy-catalog.materials.read", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", "legacy-catalog.materials.read"))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();
app.UseStandardMiddleware();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapDefaultEndpoints("intranet-bff");
app.MapStaticAssets().AllowAnonymous();

app.MapGet("/bff/session", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    var identity = context.User.Identity;
    return Results.Ok(new EmployeeSessionSummary(
        identity?.IsAuthenticated == true,
        context.User.FindFirstValue(ClaimTypes.NameIdentifier),
        identity?.Name,
        context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
        tokens.RequestToken));
}).AllowAnonymous();

app.MapPost("/bff/login", async (
    EmployeeSignInRequest request,
    HttpContext context,
    ILegacyAuthClient authClient,
    EmployeeSessionService sessions,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new { member, message = result.ErrorMessage ?? "The value is invalid." }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    EmployeeLoginResult login;
    try
    {
        login = await authClient.LoginAsync(request.Email.Trim(), request.Password, cancellationToken);
    }
    catch (LegacyAuthRateLimitedException exception)
    {
        if (exception.RetryAfterSeconds is { } retryAfterSeconds)
        {
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(
            title: "Authentication throttled",
            detail: "Too many sign-in attempts. Wait and try again.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
        logger.LogWarning(exception, "Employee authentication service is unavailable.");
        return Results.Problem(
            title: "Authentication unavailable",
            detail: "Employee sign-in is temporarily unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!login.Succeeded)
    {
        return Results.Problem(
            title: "Authentication failed",
            detail: "The email or password is invalid.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    await sessions.SignInAsync(context, login);
    return Results.Ok(new EmployeeSignInResponse(LocalReturnUrl.Normalize(request.ReturnUrl)));
})
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireRateLimiting("employee-login")
    .AllowAnonymous();

app.MapPost("/bff/logout", async (
    HttpContext context,
    EmployeeSessionService sessions,
    CancellationToken cancellationToken) =>
{
    await sessions.SignOutAsync(context, cancellationToken);
    return Results.NoContent();
}).AddEndpointFilter<AntiforgeryValidationFilter>().RequireAuthorization();

app.MapGet("/bff/catalog/materials", async (
    CatalogMaterialSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    CatalogMaterialsProxy catalog,
    EmployeeSessionService sessions,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? CatalogMaterialSort.MaterialId_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 100, 1, 250);
    var accessToken = await sessions.GetAccessTokenAsync(context, cancellationToken);
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        return Results.Unauthorized();
    }

    using var response = await catalog.GetAsync(normalizedSort, search, normalizedIndex, normalizedSize, accessToken, cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.Ok(new CatalogMaterialPage([], normalizedIndex, 0, 0, false, normalizedIndex > 1));
    }

    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
    {
        return Results.StatusCode((int)response.StatusCode);
    }

    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }

    try
    {
        var page = await response.Content.ReadFromJsonAsync<CatalogMaterialPage>(cancellationToken);
        return page is null
            ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response")
            : Results.Ok(page);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response");
    }
}).RequireAuthorization("legacy-catalog.materials.read");

app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

/// <summary>Same-origin security and proxy boundary for the legacy Intranet WASM client.</summary>
public partial class Program;
