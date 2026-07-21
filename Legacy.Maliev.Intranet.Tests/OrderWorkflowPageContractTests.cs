using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Materials;
using Legacy.Maliev.Intranet.Orders;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.RegularExpressions;
using EmployeePage = Legacy.Maliev.Intranet.Employees.PaginatedResponse<Legacy.Maliev.Intranet.Employees.EmployeeResponse>;
using OrderPage = Legacy.Maliev.Intranet.Orders.PaginatedResponse<Legacy.Maliev.Intranet.Orders.OrderResponse>;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class OrderWorkflowPageContractTests
{
    [Fact]
    public async Task CreateAndView_PreserveOrderStatusNotificationAndConcurrencyWorkflow()
    {
        var boundaries = new WorkflowBoundaries();
        await using var factory = new WorkflowFactory(boundaries);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);

        var createPage = await client.GetStringAsync("/Orders/Create?customerId=42");
        Assert.Contains("Create order", createPage, StringComparison.Ordinal);
        var createToken = AntiForgeryToken().Match(createPage).Groups[1].Value;
        using var createForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.CustomerId"] = "42",
            ["Input.Name"] = "Thai fixture",
            ["Input.Description"] = "ไม้เอก ไม้โท",
            ["Input.ProcessId"] = "3",
            ["Input.MaterialId"] = "5",
            ["Input.ColorId"] = "4",
            ["Input.SurfaceFinishId"] = "6",
            ["Input.Quantity"] = "5",
            ["Input.AllowSocialMedia"] = "true",
            ["Input.SendConfirmationEmail"] = "true",
            ["__RequestVerificationToken"] = createToken,
        });

        var created = await client.PostAsync("/Orders/Create", createForm);

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/Orders/View?id=84", created.Headers.Location?.ToString());
        Assert.Equal("Thai fixture", boundaries.Orders.CreatedRequest?.Name);
        Assert.Equal(84, boundaries.Orders.NewStatusOrderId);
        Assert.Equal("customer@example.com", boundaries.Notifications.Email);

        var viewPage = await client.GetStringAsync("/Orders/View?id=84");
        Assert.Contains("Order #84", viewPage, StringComparison.Ordinal);
        Assert.Contains("New", viewPage, StringComparison.Ordinal);
        Assert.Contains("fixture.stl", viewPage, StringComparison.Ordinal);
        var updateToken = AntiForgeryToken().Match(viewPage).Groups[1].Value;
        using var updateForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.CustomerId"] = "42",
            ["Input.EmployeeId"] = "7",
            ["Input.Name"] = "Updated fixture",
            ["Input.ProcessId"] = "3",
            ["Input.MaterialId"] = "5",
            ["Input.ColorId"] = "4",
            ["Input.SurfaceFinishId"] = "6",
            ["Input.Quantity"] = "5",
            ["Input.Manufactured"] = "2",
            ["Input.UnitPrice"] = "125",
            ["Input.DiscountPercent"] = "10",
            ["Input.CurrencyId"] = "1",
            ["Input.AllowSocialMedia"] = "true",
            ["Input.AllowCancellation"] = "true",
            ["Input.ModifiedDate"] = "2030-07-15T08:30:00Z",
            ["__RequestVerificationToken"] = updateToken,
        });

        var updated = await client.PostAsync("/Orders/View?id=84&handler=Update", updateForm);

        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        Assert.Equal("Updated fixture", boundaries.Orders.UpdatedRequest?.Name);
        Assert.Equal(new DateTimeOffset(2030, 7, 15, 8, 30, 0, TimeSpan.Zero), boundaries.Orders.ExpectedModifiedDate);

        var refreshedView = await client.GetStringAsync("/Orders/View?id=84");
        var statusToken = AntiForgeryToken().Match(refreshedView).Groups[1].Value;
        using var statusForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["statusId"] = "2",
            ["__RequestVerificationToken"] = statusToken,
        });
        var transitioned = await client.PostAsync("/Orders/View?id=84&handler=Status", statusForm);
        Assert.Equal(HttpStatusCode.Redirect, transitioned.StatusCode);
        Assert.Equal(2, boundaries.Orders.TransitionStatusId);
        Assert.False(boundaries.Orders.UpdatedRequest?.AllowCancellation);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var page = await client.GetStringAsync("/Login");
        var token = AntiForgeryToken().Match(page).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "employee@maliev.com",
            ["Password"] = "password",
            ["__RequestVerificationToken"] = token,
        });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Login", form)).StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryToken();

    private sealed class WorkflowFactory(WorkflowBoundaries boundaries) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>(); services.AddSingleton<ILegacyAuthClient>(new StubAuthClient());
                services.RemoveAll<ILegacyOrderClient>(); services.AddSingleton<ILegacyOrderClient>(boundaries.Orders);
                services.RemoveAll<ILegacyCustomerClient>(); services.AddSingleton<ILegacyCustomerClient>(boundaries.Customers);
                services.RemoveAll<ILegacyEmployeeClient>(); services.AddSingleton<ILegacyEmployeeClient>(boundaries.Employees);
                services.RemoveAll<ILegacyCatalogClient>(); services.AddSingleton<ILegacyCatalogClient>(boundaries.Catalog);
                services.RemoveAll<ILegacyFileClient>(); services.AddSingleton<ILegacyFileClient>(boundaries.Files);
                services.RemoveAll<IOrderDocumentClient>(); services.AddSingleton<IOrderDocumentClient>(boundaries.Documents);
                services.RemoveAll<ILegacyOrderNotificationClient>(); services.AddSingleton<ILegacyOrderNotificationClient>(boundaries.Notifications);
            });
        }
    }

    private sealed class WorkflowBoundaries
    {
        public StubOrderClient Orders { get; } = new();
        public StubCustomerClient Customers { get; } = new();
        public StubEmployeeClient Employees { get; } = new();
        public StubCatalogClient Catalog { get; } = new();
        public StubFileClient Files { get; } = new();
        public StubDocumentClient Documents { get; } = new();
        public StubNotificationClient Notifications { get; } = new();
    }

    private sealed class StubOrderClient : ILegacyOrderClient
    {
        private static readonly DateTime Modified = new(2030, 7, 15, 8, 30, 0, DateTimeKind.Utc);
        private static readonly OrderResponse Order = new(84, 42, 7, "Thai fixture", "ไม้เอก ไม้โท", 3, 5, 6, 4, 5, 2, 3, 125m, 10m, 562.5m, 1, 3, new(2030, 7, 20), null, null, null, true, true, false, null, new(2030, 7, 14), Modified);
        public UpsertOrderRequest? CreatedRequest { get; private set; }
        public UpsertOrderRequest? UpdatedRequest { get; private set; }
        public DateTimeOffset? ExpectedModifiedDate { get; private set; }
        public int? NewStatusOrderId { get; private set; }
        public int? TransitionStatusId { get; private set; }
        public Task<OrderResponse> CreateOrderAsync(UpsertOrderRequest request, string token, CancellationToken cancellationToken) { CreatedRequest = request; return Task.FromResult(Order); }
        public Task CreateNewOrderStatusAsync(int orderId, string token, CancellationToken cancellationToken) { NewStatusOrderId = orderId; return Task.CompletedTask; }
        public Task<OrderResponse?> GetOrderAsync(int id, string token, CancellationToken cancellationToken) => Task.FromResult<OrderResponse?>(Order);
        public Task UpdateOrderAsync(int id, UpsertOrderRequest request, DateTimeOffset? expectedModifiedDate, string token, CancellationToken cancellationToken) { UpdatedRequest = request; ExpectedModifiedDate = expectedModifiedDate; return Task.CompletedTask; }
        public Task<OrderPage?> GetOrdersAsync(OrderSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken) => Task.FromResult<OrderPage?>(new([Order], 1, 1, 1));
        public Task<OrderPage?> GetPendingOrdersAsync(int size, string token, CancellationToken cancellationToken) => Task.FromResult<OrderPage?>(new([Order], 1, 1, 1));
        public Task<IReadOnlyList<ProcessResponse>> GetProcessesAsync(string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProcessResponse>>([new(3, 1, "FDM", null, null)]);
        public Task<OrderStatusResponse?> GetLatestStatusAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<OrderStatusResponse?>(new(1, "New", null, null, null));
        public Task<IReadOnlyList<OrderStatusHistoryResponse>> GetStatusHistoryAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderStatusHistoryResponse>>([new(1, orderId, 1, "New", null, DateTime.UtcNow, null)]);
        public Task<IReadOnlyList<OrderStatusResponse>> GetAvailableStatusesAsync(int currentStatusId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderStatusResponse>>([new(2, "Accepted", null, null, null)]);
        public Task<IReadOnlyList<OrderFileResponse>> GetOrderFilesAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderFileResponse>>([new(9, orderId, "maliev.com", "uploads/42/fixture.stl")]);
        public Task<OrderFileResponse> CreateOrderFileAsync(int orderId, string bucket, string objectName, string token, CancellationToken cancellationToken) => Task.FromResult(new OrderFileResponse(9, orderId, bucket, objectName));
        public Task TransitionOrderAsync(int orderId, int statusId, string token, CancellationToken cancellationToken) { TransitionStatusId = statusId; return Task.CompletedTask; }
        public Task DeleteOrderFileAsync(int fileId, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteOrderAsync(int id, string token, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCustomerClient : ILegacyCustomerClient
    {
        private static readonly CustomerResponse Customer = new(42, "Ada", "Lovelace", "Ada Lovelace", null, null, null, "customer@example.com", null, null, null, null, null, null, null, null, null);
        public Task<CustomerResponse?> GetCustomerAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerResponse?>(Customer);
        public Task<Legacy.Maliev.Intranet.Customers.PaginatedResponse<CustomerResponse>?> GetCustomersAsync(CustomerSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) => Task.FromResult<Legacy.Maliev.Intranet.Customers.PaginatedResponse<CustomerResponse>?>(new([Customer], 1, 1, 1));
        public Task<CustomerResponse> CreateCustomerAsync(UpsertCustomerRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult(Customer);
        public Task DeleteCustomerAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubEmployeeClient : ILegacyEmployeeClient
    {
        private static readonly EmployeeResponse Employee = new(7, null, "Natt", "Maliev", "Natt Maliev", null, "employee@maliev.com", null, null, null, null, null, null);
        public Task<EmployeePage?> GetEmployeesAsync(EmployeeSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeePage?>(new([Employee], 1, 1, 1));
        public Task<EmployeeResponse?> GetEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeResponse?>(Employee);
        public Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult(Employee);
        public Task DeleteEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCatalogClient : ILegacyCatalogClient
    {
        private static readonly MaterialResponse Material = new(
            Id: 5, MaterialGroupId: 1, Machinable: false, Printable: true, Name: "PLA",
            Aisi: null, Din: null, Bts: null, Jis: null, Uns: null, En: null, Afnor: null,
            Uni: null, Sis: null, Sae: null, Astm: null, Ams: null, MaterialNumber: null,
            ManufacturerReference: null, HardnessBrinell: null, HardnessKnoop: null,
            HardnessRockwellA: null, HardnessRockwellB: null, HardnessRockwellC: null,
            HardnessVickers: null, DensityKilogramPerCubicMeter: null,
            TensileStrengthUltimateGigaPascal: null, TensileStrengthYieldMegaPascal: null,
            MachinabilityPercent: null, ShearModulusGigaPascal: null,
            ThermalConductivityWattPerMeterKelvin: null, Url: null, PricePerKilogram: null,
            CurrencyId: null, Comment: null, CreatedDate: null, ModifiedDate: null, MaterialGroup: null);
        public Task<PaginatedMaterialResponse?> GetMaterialsAsync(MaterialSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) => Task.FromResult<PaginatedMaterialResponse?>(new([Material], 1, 1, 1, false, false));
        public Task<MaterialResponse?> GetMaterialAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.FromResult<MaterialResponse?>(Material);
        public Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CurrencyResponse>>([new(1, "THB", "Thai baht", null, null)]);
        public Task<IReadOnlyList<ColorResponse>> GetColorsAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ColorResponse>>([new(4, "Black", null, null)]);
        public Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SurfaceFinishResponse>>([new(6, "As printed", null, null)]);
        public Task<IReadOnlyList<ColorResponse>> GetMaterialColorsAsync(int id, string accessToken, CancellationToken cancellationToken) => GetColorsAsync(accessToken, cancellationToken);
        public Task<IReadOnlyList<SurfaceFinishResponse>> GetMaterialSurfaceFinishesAsync(int id, string accessToken, CancellationToken cancellationToken) => GetSurfaceFinishesAsync(accessToken, cancellationToken);
        public Task<IReadOnlyList<CountryResponse>> GetCountriesAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CountryResponse>>([]);
        public Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MaterialGroupResponse>>([]);
        public Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult(Material);
        public Task UpdateMaterialAsync(int id, UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SyncMaterialColorsAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SyncMaterialSurfaceFinishesAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubFileClient : ILegacyFileClient
    {
        public Task<IReadOnlyList<UploadObjectResponse>> UploadOrderFilesAsync(int customerId, IReadOnlyList<Microsoft.AspNetCore.Http.IFormFile> files, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UploadObjectResponse>>([]);
        public Task<UploadObjectResponse> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, string token, CancellationToken cancellationToken) => Task.FromResult<Uri?>(new("https://storage.test/fixture"));
        public Task DeleteAsync(string bucket, string objectName, string token, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubDocumentClient : IOrderDocumentClient
    {
        public Task<byte[]> RenderOrderLabelAsync(OrderLabelRequest request, string token, CancellationToken cancellationToken) => Task.FromResult("%PDF"u8.ToArray());
    }

    private sealed class StubNotificationClient : ILegacyOrderNotificationClient
    {
        public string? Email { get; private set; }
        public Task SendCreatedAsync(string email, int orderId, string token, CancellationToken cancellationToken) { Email = email; return Task.CompletedTask; }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("employee-token", "refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(14)), new("employee-id", email, email)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }
}
