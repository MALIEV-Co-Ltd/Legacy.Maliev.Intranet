using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Orders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.RegularExpressions;
using EmployeePageResponse = Legacy.Maliev.Intranet.Employees.PaginatedResponse<Legacy.Maliev.Intranet.Employees.EmployeeResponse>;
using OrderPageResponse = Legacy.Maliev.Intranet.Orders.PaginatedResponse<Legacy.Maliev.Intranet.Orders.OrderResponse>;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class OrderPageContractTests
{
    [Fact]
    public async Task AuthenticatedOrdersPage_RendersAssignedPendingAndAllOrders()
    {
        var orders = new StubOrderClient();
        await using var factory = new OrderIntranetFactory(orders, new StubEmployeeClient(), new StubAuthClient());
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);

        var response = await client.GetAsync("/Orders/Index?search=fixture&index=1&size=25");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Assigned to me", html, StringComparison.Ordinal);
        Assert.Contains("Assigned fixture", html, StringComparison.Ordinal);
        Assert.Contains("Awaiting assignment", html, StringComparison.Ordinal);
        Assert.Contains("Unassigned fixture", html, StringComparison.Ordinal);
        Assert.Contains("All orders", html, StringComparison.Ordinal);
        Assert.Contains("Completed fixture", html, StringComparison.Ordinal);
        Assert.Contains("Natt Maliev", html, StringComparison.Ordinal);
        Assert.Equal("employee-token", orders.LastAccessToken);
        Assert.Equal("fixture", orders.LastSearch);
        Assert.Equal(25, orders.LastSize);
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
        var response = await client.PostAsync("/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryToken();

    private sealed class OrderIntranetFactory(
        ILegacyOrderClient orders,
        ILegacyEmployeeClient employees,
        ILegacyAuthClient auth) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyOrderClient>();
                services.RemoveAll<ILegacyEmployeeClient>();
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton(orders);
                services.AddSingleton(employees);
                services.AddSingleton(auth);
            });
        }
    }

    private sealed class StubOrderClient : ILegacyOrderClient
    {
        private static readonly OrderResponse Assigned = Order(101, 7, "Assigned fixture");
        private static readonly OrderResponse Unassigned = Order(102, null, "Unassigned fixture");
        private static readonly OrderResponse Completed = Order(103, 7, "Completed fixture");

        public string? LastAccessToken { get; private set; }
        public string? LastSearch { get; private set; }
        public int LastSize { get; private set; }

        public Task<OrderPageResponse?> GetOrdersAsync(OrderSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken)
        {
            LastAccessToken = token;
            LastSearch = search;
            LastSize = size;
            return Task.FromResult<OrderPageResponse?>(new([Completed], index, 1, 1));
        }

        public Task<OrderPageResponse?> GetPendingOrdersAsync(int size, string token, CancellationToken cancellationToken) =>
            Task.FromResult<OrderPageResponse?>(new([Assigned, Unassigned], 1, 1, 2));

        public Task<IReadOnlyList<ProcessResponse>> GetProcessesAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessResponse>>([new(3, 1, "FDM", null, null)]);

        public Task<OrderResponse?> GetOrderAsync(int id, string token, CancellationToken cancellationToken) => Task.FromResult<OrderResponse?>(Completed);
        public Task<OrderResponse> CreateOrderAsync(UpsertOrderRequest request, string token, CancellationToken cancellationToken) => Task.FromResult(Completed);
        public Task UpdateOrderAsync(int id, UpsertOrderRequest request, DateTimeOffset? expectedModifiedDate, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteOrderAsync(int id, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CreateNewOrderStatusAsync(int orderId, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<OrderStatusResponse?> GetLatestStatusAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<OrderStatusResponse?>(null);
        public Task<IReadOnlyList<OrderStatusHistoryResponse>> GetStatusHistoryAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderStatusHistoryResponse>>([]);
        public Task<IReadOnlyList<OrderStatusResponse>> GetAvailableStatusesAsync(int currentStatusId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderStatusResponse>>([]);
        public Task TransitionOrderAsync(int orderId, int statusId, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<OrderFileResponse>> GetOrderFilesAsync(int orderId, string token, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrderFileResponse>>([]);
        public Task<OrderFileResponse> CreateOrderFileAsync(int orderId, string bucket, string objectName, string token, CancellationToken cancellationToken) => Task.FromResult(new OrderFileResponse(1, orderId, bucket, objectName));
        public Task DeleteOrderFileAsync(int fileId, string token, CancellationToken cancellationToken) => Task.CompletedTask;

        private static OrderResponse Order(int id, int? employeeId, string name) => new(
            id, 42, employeeId, name, null, 3, null, null, null, 5, 2, 3, 125m, 10m,
            562.50m, 1, 3, new DateTime(2030, 7, 20), null, null, null, true, true, true,
            null, new DateTime(2030, 7, 15), null);
    }

    private sealed class StubEmployeeClient : ILegacyEmployeeClient
    {
        private static readonly EmployeeResponse Employee = new(
            7, null, "Natt", "Maliev", "Natt Maliev", null, "employee@maliev.com", null,
            null, null, null, null, null);

        public Task<EmployeePageResponse?> GetEmployeesAsync(EmployeeSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeePageResponse?>(new([Employee], 1, 1, 1));
        public Task<EmployeeResponse?> GetEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeResponse?>(Employee);
        public Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult(Employee);
        public Task DeleteEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true, new("employee-token", "refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(14)), new("employee-id", email, email)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }
}
