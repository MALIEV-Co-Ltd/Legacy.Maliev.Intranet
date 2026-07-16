using System.Security.Claims;
using Legacy.Maliev.Intranet.Bff;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.Services.AddProblemDetails();
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
        options.SlidingExpiration = false;
    });
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
    new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();
app.UseStandardMiddleware();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapDefaultEndpoints("intranet-bff");

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

app.MapPost("/bff/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).AddEndpointFilter<AntiforgeryValidationFilter>().RequireAuthorization();

await app.RunAsync();

/// <summary>Same-origin security and proxy boundary for the legacy Intranet WASM client.</summary>
public partial class Program;
