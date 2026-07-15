using Legacy.Maliev.Intranet;
using Legacy.Maliev.Intranet.Auth;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("legacy-intranet:");
}
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DistributedTicketStore>();
builder.Services.AddScoped<EmployeeSessionService>();
builder.Services.AddHttpClient<ILegacyAuthClient, LegacyAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
});
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
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<DistributedTicketStore>((options, store) => options.SessionStore = store);
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
app.MapPost("/Logout", async (HttpContext context, EmployeeSessionService sessions, CancellationToken cancellationToken) =>
{
    await sessions.SignOutAsync(context, cancellationToken);
    return Results.LocalRedirect("/Login");
}).RequireAuthorization();

await app.RunAsync();

/// <summary>Legacy Intranet BFF entry point.</summary>
public partial class Program;