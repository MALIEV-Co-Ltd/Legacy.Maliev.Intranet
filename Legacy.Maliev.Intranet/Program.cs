using Legacy.Maliev.Intranet;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.Services.AddProblemDetails();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Legacy.Maliev.Intranet";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
    });
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
    new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    foreach (var route in LegacyRoutes.Anonymous.Where(route => route.StartsWith("/Employees/", StringComparison.Ordinal)))
    {
        options.Conventions.AddPageRoute("/AnonymousLegacyRoute", route);
    }
    options.Conventions.AllowAnonymousToPage("/AnonymousLegacyRoute");

    foreach (var route in LegacyRoutes.All.Where(route =>
                 !LegacyRoutes.Anonymous.Contains(route) &&
                 route is not "/Dashboard" and not "/AccessDenied"))
    {
        options.Conventions.AddPageRoute("/LegacyRoute", route);
    }
});

var app = builder.Build();

app.UseStandardMiddleware();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("intranet");
app.MapRazorPages();

await app.RunAsync();

/// <summary>Legacy Intranet BFF entry point.</summary>
public partial class Program;