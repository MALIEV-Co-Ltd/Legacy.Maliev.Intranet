using System.Security.Claims;
using Legacy.Maliev.Intranet.Bff;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Bff.Catalog;
using Legacy.Maliev.Intranet.Bff.Customers;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Bff.Procurement;
using Legacy.Maliev.Intranet.Server.Orders;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Polly;

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
builder.Services.AddOptions<ServiceAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection("ServiceAuthentication"));
builder.Services.AddSingleton<IServiceAccessTokenProvider, ServiceAccessTokenProvider>();
builder.Services.AddTransient<LegacyServiceAuthenticationHandler>();
builder.Services.AddScoped<Legacy.Maliev.Intranet.Customers.CustomerAccountCreationService>();
builder.Services.AddScoped<Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationService>();
#pragma warning disable EXTEXP0001 // Replace inherited pipelines with explicit downstream 429 contracts.
builder.Services.AddHttpClient<Legacy.Maliev.Intranet.Customers.ICustomerProfileCreationClient, Legacy.Maliev.Intranet.Customers.CustomerProfileCreationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Customer"]
        ?? throw new InvalidOperationException("Services:Customer is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<Legacy.Maliev.Intranet.Customers.ICustomerIdentityCreationClient, Legacy.Maliev.Intranet.Customers.CustomerIdentityCreationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<Legacy.Maliev.Intranet.Employees.IEmployeeProfileCreationClient, Legacy.Maliev.Intranet.Employees.EmployeeProfileCreationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Employee"]
        ?? throw new InvalidOperationException("Services:Employee is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<Legacy.Maliev.Intranet.Employees.IEmployeeIdentityCreationClient, Legacy.Maliev.Intranet.Employees.EmployeeIdentityCreationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<CustomersProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Customer"]
        ?? throw new InvalidOperationException("Services:Customer is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("customer-list", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
builder.Services.AddHttpClient<EmployeesProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Employee"]
        ?? throw new InvalidOperationException("Services:Employee is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("employee-list", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
builder.Services.AddHttpClient<OrdersProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Order"]
        ?? throw new InvalidOperationException("Services:Order is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("order-index", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
builder.Services.AddHttpClient<SuppliersProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Procurement"]
        ?? throw new InvalidOperationException("Services:Procurement is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("supplier-index", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
builder.Services.AddHttpClient<PurchaseOrdersProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Procurement"]
        ?? throw new InvalidOperationException("Services:Procurement is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("purchase-order-index", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
builder.Services.AddHttpClient<OrderDetailProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Order"]
        ?? throw new InvalidOperationException("Services:Order is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<OrderCreateProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Order"]
        ?? throw new InvalidOperationException("Services:Order is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<OrderCatalogReferenceProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<OrderEmployeeReferenceProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Employee"]
        ?? throw new InvalidOperationException("Services:Employee is required."));
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<LegacyServiceAuthenticationHandler>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<OrderFileProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:File"]
        ?? "https+http://legacy-maliev-file-service");
    client.Timeout = TimeSpan.FromMinutes(5);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<OrderDocumentProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Document"]
        ?? "https+http://legacy-maliev-document-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddHttpClient<OrderNotificationProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Notification"]
        ?? "https+http://legacy-maliev-notification-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>();
builder.Services.AddScoped<OrderDetailAggregator>();
builder.Services.AddScoped<OrderFileWorkflow>();
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IOrderCreationStateStore, InMemoryOrderCreationStateStore>();
}
else
{
    builder.Services.AddSingleton<IOrderCreationStateStore, RedisOrderCreationStateStore>();
}
builder.Services.AddScoped<OrderCreationWorkflow>();
builder.Services.AddHttpClient<CatalogMaterialsProxy>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
        ?? throw new InvalidOperationException("Services:Catalog is required."));
    client.Timeout = TimeSpan.FromSeconds(10);
}).RemoveAllResilienceHandlers()
    .AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()
    .AddResilienceHandler("catalog-materials", pipeline =>
{
    pipeline.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = Polly.DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldRetryAfterHeader = false,
        ShouldHandle = new Polly.PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<Polly.Timeout.TimeoutRejectedException>()
            .HandleResult(response => response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError),
    });
});
#pragma warning restore EXTEXP0001
builder.Services.AddHttpClient("service-auth", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Auth"]
        ?? throw new InvalidOperationException("Services:Auth is required."));
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
    .AddPolicy(LegacyEmployeePermissions.CustomersRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.CustomersRead))
    .AddPolicy(LegacyEmployeePermissions.CustomersCreate, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.CustomersCreate))
    .AddPolicy(LegacyEmployeePermissions.CustomersList, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.CustomersList))
    .AddPolicy(LegacyEmployeePermissions.EmployeesList, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.EmployeesList))
    .AddPolicy(LegacyEmployeePermissions.EmployeesCreate, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.EmployeesCreate))
    .AddPolicy(LegacyEmployeePermissions.EmployeesRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.EmployeesRead))
    .AddPolicy(LegacyEmployeePermissions.OrdersRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead))
    .AddPolicy(LegacyEmployeePermissions.OrdersCreate, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrdersCreate))
    .AddPolicy(LegacyEmployeePermissions.OrderCatalogRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderCatalogRead))
    .AddPolicy(LegacyEmployeePermissions.OrdersUpdate, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrdersUpdate))
    .AddPolicy(LegacyEmployeePermissions.OrderStatusRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderStatusRead))
    .AddPolicy(LegacyEmployeePermissions.OrderStatusWrite, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderStatusWrite))
    .AddPolicy(LegacyEmployeePermissions.OrderFilesRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderFilesRead)
        .RequireClaim("permissions", LegacyEmployeePermissions.FileUploadsRead))
    .AddPolicy(LegacyEmployeePermissions.OrderFilesWrite, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderFilesWrite)
        .RequireClaim("permissions", LegacyEmployeePermissions.FileUploadsCreate))
    .AddPolicy(LegacyEmployeePermissions.OrderFilesDelete, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.OrderFilesDelete)
        .RequireClaim("permissions", LegacyEmployeePermissions.FileUploadsDelete))
    .AddPolicy(LegacyEmployeePermissions.SuppliersRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.SuppliersRead))
    .AddPolicy(LegacyEmployeePermissions.PurchaseOrdersRead, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", LegacyEmployeePermissions.PurchaseOrdersRead))
    .AddPolicy("legacy-catalog.materials.read", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", "legacy-catalog.materials.read"))
    .AddPolicy("legacy-catalog.materials.create", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", "legacy-catalog.materials.create"))
    .AddPolicy("legacy-catalog.materials.update", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permissions", "legacy-catalog.materials.update"))
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
        tokens.RequestToken,
        int.TryParse(
            context.User.FindFirstValue(EmployeeSessionService.LegacyDatabaseIdClaim),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var legacyDatabaseId) && legacyDatabaseId > 0
                ? legacyDatabaseId
                : null));
}).AllowAnonymous();

app.MapGet("/bff/orders", (
    OrderListSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    OrdersProxy orders,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? OrderListSort.OrderCreatedDate_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 25, 1, 250);
    return OrdersEndpointMapper.MapPageAsync(
        token => orders.GetAsync(normalizedSort, search, normalizedIndex, normalizedSize, token),
        normalizedIndex,
        context,
        cancellationToken);
})
    .RequireAuthorization(LegacyEmployeePermissions.OrdersRead);

app.MapGet("/bff/suppliers", (
    SupplierListSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    SuppliersProxy suppliers,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? SupplierListSort.SupplierId_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 100, 1, 250);
    return SuppliersEndpointMapper.MapPageAsync(
        token => suppliers.GetAsync(normalizedSort, search, normalizedIndex, normalizedSize, token),
        normalizedIndex,
        context,
        cancellationToken);
})
    .RequireAuthorization(LegacyEmployeePermissions.SuppliersRead);

app.MapGet("/bff/purchase-orders", (
    PurchaseOrderListSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    PurchaseOrdersProxy purchaseOrders,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort is { } requestedSort && Enum.IsDefined(requestedSort)
        ? requestedSort
        : PurchaseOrderListSort.PurchaseOrderId_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 25, 1, 250);
    return PurchaseOrdersEndpointMapper.MapPageAsync(
        token => purchaseOrders.GetAsync(normalizedSort, search, normalizedIndex, normalizedSize, token),
        normalizedIndex,
        context,
        cancellationToken);
})
    .RequireAuthorization(LegacyEmployeePermissions.PurchaseOrdersRead);

app.MapGet("/bff/orders/pending", (
    int? size,
    HttpContext context,
    OrdersProxy orders,
    CancellationToken cancellationToken) =>
{
    var normalizedSize = Math.Clamp(size ?? 1000, 1, 1000);
    return OrdersEndpointMapper.MapPageAsync(
        token => orders.GetPendingAsync(normalizedSize, token),
        1,
        context,
        cancellationToken);
})
    .RequireAuthorization(LegacyEmployeePermissions.OrdersRead);

app.MapGet("/bff/order-processes", (
    HttpContext context,
    OrdersProxy orders,
    CancellationToken cancellationToken) =>
    OrdersEndpointMapper.MapProcessesAsync(orders.GetProcessesAsync, context, cancellationToken))
    .RequireAuthorization(LegacyEmployeePermissions.OrderCatalogRead);

app.MapGet("/bff/orders/create", (
    int? customerId,
    OrdersProxy orders,
    OrderCatalogReferenceProxy catalog,
    CustomersProxy customers,
    CancellationToken cancellationToken) =>
    OrderCreateEndpointMapper.GetAsync(customerId, orders, catalog, customers, cancellationToken))
    .RequireAuthorization(policy =>
    {
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrdersCreate);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrderCatalogRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.CustomersRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.CatalogMaterialsRead);
    });

app.MapGet("/bff/orders/create/materials/{materialId:int}", (
    int materialId,
    OrderCatalogReferenceProxy catalog,
    CancellationToken cancellationToken) =>
    OrderCreateEndpointMapper.GetMaterialOptionsAsync(materialId, catalog, cancellationToken))
    .RequireAuthorization(policy =>
    {
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrdersCreate);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.CatalogMaterialsRead);
    });

app.MapPost("/bff/orders", (
    HttpRequest request,
    HttpContext context,
    CustomersProxy customers,
    OrdersProxy orderReferences,
    OrderCatalogReferenceProxy catalog,
    OrderCreateProxy orders,
    OrderDetailProxy orderFiles,
    OrderFileProxy files,
    OrderNotificationProxy notifications,
    OrderCreationWorkflow workflow,
    ILogger<OrderCreationWorkflow> logger,
    CancellationToken cancellationToken) =>
    OrderCreateEndpointMapper.CreateAsync(
        request,
        context,
        customers,
        orderReferences,
        catalog,
        orders,
        orderFiles,
        files,
        notifications,
        workflow,
        logger,
        cancellationToken))
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(201L * 1024 * 1024))
    .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = 201L * 1024 * 1024 })
    .RequireAuthorization(LegacyEmployeePermissions.OrdersCreate);

app.MapGet("/bff/orders/{id:int}", (
    int id,
    OrderDetailAggregator aggregator,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.GetAsync(id, aggregator, cancellationToken))
    .RequireAuthorization(policy =>
    {
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrderCatalogRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.EmployeesList);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.CatalogMaterialsRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrderStatusRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrderFilesRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.FileUploadsRead);
    });

app.MapPut("/bff/orders/{id:int}", (
    int id,
    OrderUpdateRequest input,
    OrderDetailProxy orders,
    HttpContext context,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.UpdateAsync(id, input, orders, context, cancellationToken))
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization(LegacyEmployeePermissions.OrdersUpdate);

app.MapPost("/bff/orders/{id:int}/status/{statusId:int}", (
    int id,
    int statusId,
    HttpRequest request,
    OrderDetailProxy orders,
    HttpContext context,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.TransitionAsync(
        id,
        statusId,
        request.Headers["Idempotency-Key"].FirstOrDefault(),
        orders,
        context,
        cancellationToken))
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization(LegacyEmployeePermissions.OrderStatusWrite);

app.MapPost("/bff/orders/{id:int}/files", (
    int id,
    HttpRequest request,
    OrderDetailProxy orders,
    OrderFileProxy files,
    OrderFileWorkflow workflow,
    ILogger<OrderFileWorkflow> logger,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.UploadAsync(id, request, orders, files, workflow, logger, cancellationToken))
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(200L * 1024 * 1024))
    .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = 200L * 1024 * 1024 })
    .RequireAuthorization(LegacyEmployeePermissions.OrderFilesWrite);

app.MapDelete("/bff/orders/{id:int}/files/{fileId:int}", (
    int id,
    int fileId,
    OrderDetailProxy orders,
    OrderFileProxy files,
    OrderFileWorkflow workflow,
    ILogger<OrderFileWorkflow> logger,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.RemoveFileAsync(id, fileId, orders, files, workflow, logger, cancellationToken))
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization(LegacyEmployeePermissions.OrderFilesDelete);

app.MapGet("/bff/orders/{id:int}/label", (
    int id,
    OrderDetailAggregator aggregator,
    OrderDocumentProxy documents,
    CancellationToken cancellationToken) =>
    OrderDetailEndpointMapper.LabelAsync(id, aggregator, documents, cancellationToken))
    .RequireAuthorization(policy =>
    {
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrdersRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.OrderCatalogRead);
        policy.RequireClaim("permissions", LegacyEmployeePermissions.CatalogMaterialsRead);
    });

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

app.MapGet("/bff/customers", async (
    CustomerListSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    CustomersProxy customers,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? CustomerListSort.CustomerCreatedDate_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 25, 1, 250);
    HttpResponseMessage response;
    try
    {
        response = await customers.GetAsync(
            normalizedSort,
            search,
            normalizedIndex,
            normalizedSize,
            cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Ok(new CustomerListPage([], normalizedIndex, 0, 0, false, normalizedIndex > 1));
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
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
        }

        try
        {
            var page = await response.Content.ReadFromJsonAsync<CustomerListPage>(cancellationToken);
            return page is null
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response")
                : Results.Ok(page);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response");
        }
    }
})
    .RequireAuthorization(LegacyEmployeePermissions.CustomersList);

app.MapGet("/bff/customers/{id:int}", async (
    int id,
    HttpContext context,
    CustomersProxy customers,
    CancellationToken cancellationToken) =>
{
    HttpResponseMessage response;
    try
    {
        response = await customers.GetByIdAsync(id, cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.NotFound)
        {
            return Results.StatusCode((int)response.StatusCode);
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

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
        }

        try
        {
            var customer = await response.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
            var invalid = customer is null ||
                customer.Id != id ||
                string.IsNullOrWhiteSpace(customer.FirstName) ||
                string.IsNullOrWhiteSpace(customer.LastName) ||
                string.IsNullOrWhiteSpace(customer.FullName) ||
                string.IsNullOrWhiteSpace(customer.Email) ||
                (customer.Company is not null && string.IsNullOrWhiteSpace(customer.Company.Name)) ||
                (customer.BillingAddress is not null && string.IsNullOrWhiteSpace(customer.BillingAddress.AddressLine1)) ||
                (customer.ShippingAddress is not null && string.IsNullOrWhiteSpace(customer.ShippingAddress.AddressLine1));
            return invalid
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response")
                : Results.Ok(customer);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response");
        }
    }
})
    .RequireAuthorization(LegacyEmployeePermissions.CustomersRead);

app.MapPost("/bff/customers", async (
    CreateCustomerAccountRequest request,
    HttpContext context,
    Legacy.Maliev.Intranet.Customers.CustomerAccountCreationService workflow,
    CancellationToken cancellationToken) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new
                {
                    member = string.IsNullOrEmpty(member)
                        ? member
                        : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member),
                    message = result.ErrorMessage ?? "The value is invalid.",
                }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    var result = await workflow.CreateAsync(request, cancellationToken);
    if (result.Status == Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.RateLimited &&
        result.RetryAfter is { } retryAfter)
    {
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    return result.Status switch
    {
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.Created when result.CustomerId is { } customerId =>
            Results.Created($"/Customers/View?id={customerId}", new CreatedCustomerAccount(customerId)),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.BadRequest =>
            Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Customer data was rejected"),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.Unauthorized =>
            Results.StatusCode(StatusCodes.Status401Unauthorized),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.Forbidden =>
            Results.StatusCode(StatusCodes.Status403Forbidden),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.Conflict =>
            Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Customer identity already exists"),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.RateLimited =>
            Results.StatusCode(StatusCodes.Status429TooManyRequests),
        Legacy.Maliev.Intranet.Customers.CustomerAccountCreationStatus.BadGateway =>
            Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid customer service response"),
        _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Customer creation unavailable"),
    };
})
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization(LegacyEmployeePermissions.CustomersCreate);

app.MapPost("/bff/employees", async (
    CreateEmployeeAccountRequest request,
    HttpContext context,
    Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationService workflow,
    CancellationToken cancellationToken) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new
                {
                    member = string.IsNullOrEmpty(member)
                        ? member
                        : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member),
                    message = result.ErrorMessage ?? "The value is invalid.",
                }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    var result = await workflow.CreateAsync(request, cancellationToken);
    if (result.Status == Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.RateLimited &&
        result.RetryAfter is { } retryAfter)
    {
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    return result.Status switch
    {
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.Created when result.EmployeeId is { } employeeId =>
            Results.Created($"/Employees/View?id={employeeId}", new CreatedEmployeeAccount(employeeId)),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.BadRequest =>
            Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Employee data was rejected"),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.Unauthorized =>
            Results.StatusCode(StatusCodes.Status401Unauthorized),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.Forbidden =>
            Results.StatusCode(StatusCodes.Status403Forbidden),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.Conflict =>
            Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Employee identity already exists"),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.RateLimited =>
            Results.StatusCode(StatusCodes.Status429TooManyRequests),
        Legacy.Maliev.Intranet.Employees.EmployeeAccountCreationStatus.BadGateway =>
            Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid employee service response"),
        _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Employee creation unavailable"),
    };
})
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization(LegacyEmployeePermissions.EmployeesCreate);

app.MapGet("/bff/employees/{id:int}", async (
    int id,
    HttpContext context,
    EmployeesProxy employees,
    CancellationToken cancellationToken) =>
{
    HttpResponseMessage response;
    try
    {
        response = await employees.GetByIdAsync(id, cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.NotFound)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
        }

        try
        {
            var employee = await response.Content.ReadFromJsonAsync<EmployeeDetail>(cancellationToken);
            var invalid = employee is null ||
                employee.Id != id ||
                string.IsNullOrWhiteSpace(employee.FirstName) ||
                string.IsNullOrWhiteSpace(employee.LastName) ||
                string.IsNullOrWhiteSpace(employee.FullName) ||
                string.IsNullOrWhiteSpace(employee.Email);
            return invalid
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid EmployeeService response")
                : Results.Ok(employee);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid EmployeeService response");
        }
    }
})
    .RequireAuthorization(LegacyEmployeePermissions.EmployeesRead);

app.MapGet("/bff/employees", async (
    EmployeeListSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    EmployeesProxy employees,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? EmployeeListSort.EmployeeId_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 25, 1, 250);
    HttpResponseMessage response;
    try
    {
        response = await employees.GetAsync(
            normalizedSort,
            search,
            normalizedIndex,
            normalizedSize,
            cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Ok(new EmployeeListPage([], normalizedIndex, 0, 0, false, normalizedIndex > 1));
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "EmployeeService unavailable");
        }

        try
        {
            var page = await response.Content.ReadFromJsonAsync<EmployeeListPage>(cancellationToken);
            return page is null || page.Items is null
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid EmployeeService response")
                : Results.Ok(page);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid EmployeeService response");
        }
    }
})
    .RequireAuthorization(LegacyEmployeePermissions.EmployeesList);

app.MapGet("/bff/catalog/materials", async (
    CatalogMaterialSort? sort,
    string? search,
    int? index,
    int? size,
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
{
    var normalizedSort = sort ?? CatalogMaterialSort.MaterialId_Descending;
    var normalizedIndex = Math.Max(1, index ?? 1);
    var normalizedSize = Math.Clamp(size ?? 100, 1, 250);
    HttpResponseMessage response;
    try
    {
        response = await catalog.GetAsync(
            normalizedSort,
            search,
            normalizedIndex,
            normalizedSize,
            cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
        }

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
    }
}).RequireAuthorization("legacy-catalog.materials.read");

app.MapGet("/bff/catalog/material-groups", (
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
    ProxyCatalogJsonAsync<IReadOnlyList<CatalogMaterialGroup>>(
        catalog.GetMaterialGroupsAsync,
        context,
        cancellationToken))
    .RequireAuthorization("legacy-catalog.materials.read");

app.MapGet("/bff/catalog/currencies", (
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
    ProxyCatalogJsonAsync<IReadOnlyList<CatalogCurrency>>(
        catalog.GetCurrenciesAsync,
        context,
        cancellationToken))
    .RequireAuthorization("legacy-catalog.materials.read");

app.MapPost("/bff/catalog/materials", async (
    CatalogMaterialUpsertRequest request,
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
{
    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new
                {
                    member = string.IsNullOrEmpty(member)
                        ? member
                        : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member),
                    message = result.ErrorMessage ?? "The value is invalid.",
                }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    return await ProxyCatalogJsonAsync<CatalogCreatedMaterial>(
        token => catalog.CreateAsync(request, token),
        context,
        cancellationToken,
        preserveBadRequest: true);
})
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization("legacy-catalog.materials.create");

app.MapGet("/bff/catalog/materials/{id:int}", async (
    int id,
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
{
    if (id <= 0)
    {
        return Results.NotFound();
    }

    HttpResponseMessage response;
    try
    {
        response = await catalog.GetByIdAsync(id, cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound();
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
            var detail = await response.Content.ReadFromJsonAsync<CatalogMaterialDetail>(cancellationToken);
            return detail is null
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response")
                : Results.Ok(detail);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response");
        }
    }
}).RequireAuthorization("legacy-catalog.materials.read");

app.MapPut("/bff/catalog/materials/{id:int}", async (
    int id,
    CatalogMaterialUpsertRequest request,
    HttpContext context,
    CatalogMaterialsProxy catalog,
    CancellationToken cancellationToken) =>
{
    if (id <= 0)
    {
        return Results.NotFound();
    }

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
    {
        var errors = validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new
                {
                    member = string.IsNullOrEmpty(member)
                        ? member
                        : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member),
                    message = result.ErrorMessage ?? "The value is invalid.",
                }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    return await ProxyCatalogNoContentAsync(
        token => catalog.UpdateAsync(id, request, token),
        context,
        cancellationToken,
        preserveBadRequest: true);
})
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization("legacy-catalog.materials.update");

app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();

static async Task<IResult> ProxyCatalogJsonAsync<T>(
    Func<CancellationToken, Task<HttpResponseMessage>> send,
    HttpContext context,
    CancellationToken cancellationToken,
    bool preserveBadRequest = false)
{
    HttpResponseMessage response;
    try
    {
        response = await send(cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Conflict ||
            (preserveBadRequest && response.StatusCode == System.Net.HttpStatusCode.BadRequest))
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response")
                : Results.Ok(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid Catalog response");
        }
    }
}

static async Task<IResult> ProxyCatalogNoContentAsync(
    Func<CancellationToken, Task<HttpResponseMessage>> send,
    HttpContext context,
    CancellationToken cancellationToken,
    bool preserveBadRequest = false)
{
    HttpResponseMessage response;
    try
    {
        response = await send(cancellationToken);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }

    using (response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Conflict ||
            preserveBadRequest && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        return response.IsSuccessStatusCode
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog unavailable");
    }
}

/// <summary>Same-origin security and proxy boundary for the legacy Intranet WASM client.</summary>
public partial class Program;
