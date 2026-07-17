using Legacy.Maliev.Intranet;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Materials;
using Legacy.Maliev.Intranet.Orders;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Legacy.Maliev.Intranet.Suppliers;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
const long maximumUploadBytes = 200L * 1024L * 1024L;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maximumUploadBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maximumUploadBytes);

builder.AddServiceDefaults();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddLegacyIntranetDataProtection();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddLegacyAccessTokenValidation(
    builder.Configuration,
    validateOnStart: !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddOptions<ServiceAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection("ServiceAuthentication"));
builder.Services.AddSingleton<IServiceAccessTokenProvider, ServiceAccessTokenProvider>();
builder.Services.AddTransient<LegacyServiceAuthenticationHandler>();
builder.Services.AddSingleton<DistributedTicketStore>();
builder.Services.AddScoped<EmployeeSessionService>();
builder.Services.AddScoped<OrderReferenceDataLoader>();
builder.Services.AddHttpClient<ILegacyAuthClient, LegacyAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient("service-auth", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyCustomerClient, LegacyCustomerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Customer"]
        ?? throw new InvalidOperationException("Services:Customer is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyEmployeeClient, LegacyEmployeeClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Employee"]
        ?? throw new InvalidOperationException("Services:Employee is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyCatalogClient, LegacyCatalogClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyProcurementClient, LegacyProcurementClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Procurement"]
        ?? throw new InvalidOperationException("Services:Procurement is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyOrderClient, LegacyOrderClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Order"]
        ?? throw new InvalidOperationException("Services:Order is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IPurchaseOrderDocumentClient, PurchaseOrderDocumentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Document"]
        ?? throw new InvalidOperationException("Services:Document is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<IOrderDocumentClient, OrderDocumentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Document"]
        ?? throw new InvalidOperationException("Services:Document is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyFileClient, LegacyFileClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:File"]
        ?? throw new InvalidOperationException("Services:File is required."));
    client.Timeout = TimeSpan.FromMinutes(3);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<ILegacyOrderNotificationClient, LegacyOrderNotificationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Notification"]
        ?? throw new InvalidOperationException("Services:Notification is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Legacy.Maliev.Intranet";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = LegacyCookieSecurity.ResolveSecurePolicy(builder.Environment.EnvironmentName);
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
                 !route.StartsWith("/Customers/", StringComparison.OrdinalIgnoreCase) &&
                 !route.StartsWith("/Employees/", StringComparison.OrdinalIgnoreCase) &&
                 !route.StartsWith("/Materials/", StringComparison.OrdinalIgnoreCase) &&
                 !route.StartsWith("/Orders/", StringComparison.OrdinalIgnoreCase) &&
                 !route.StartsWith("/PurchaseOrders/", StringComparison.OrdinalIgnoreCase) &&
                 !route.StartsWith("/Suppliers/", StringComparison.OrdinalIgnoreCase) &&
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
