using System.Reflection;
using System.Security.Claims;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

#pragma warning disable BL0006 // Focused tests inspect the compiled Razor render tree without adding a test-only renderer dependency.

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerComponentsBehaviorTests
{
    [Fact]
    public void Overview_LeavesMudFormAndValidationOwnershipWithItsParent()
    {
        var componentType = typeof(CustomerOverview);

        Assert.Equal(typeof(EventCallback), componentType.GetProperty(nameof(CustomerOverview.Save))?.PropertyType);
        Assert.DoesNotContain(
            componentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            field => field.FieldType == typeof(MudForm));
        Assert.DoesNotContain(
            componentType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            property => property.PropertyType == typeof(MudForm));
    }

    [Theory]
    [MemberData(nameof(InvalidPageContracts))]
    public void HistoryTable_RejectsEveryMissingExtraOrMismatchedPageContract(
        CustomerHistoryKind kind,
        bool loading,
        string? error,
        OrderListPage? orders,
        QuotationListPage? quotations,
        InvoiceListPage? invoices)
    {
        var table = CreateHistoryTable(kind, orders, quotations, invoices);
        SetComponentParameters(table, new Dictionary<string, object?>
        {
            [nameof(CustomerHistoryTable.Loading)] = loading,
            [nameof(CustomerHistoryTable.Error)] = error,
        });

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeNonPublic(table, "OnParametersSet"));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("exactly one matching page contract", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CustomerHistoryKind.Order)]
    [InlineData(CustomerHistoryKind.Quotation)]
    [InlineData(CustomerHistoryKind.Invoice)]
    public void HistoryTable_AcceptsExactlyOneMatchingPageContract(CustomerHistoryKind kind)
    {
        var table = CreateHistoryTable(
            kind,
            kind == CustomerHistoryKind.Order ? OrdersPage(2, 3) : null,
            kind == CustomerHistoryKind.Quotation ? QuotationsPage(2, 3) : null,
            kind == CustomerHistoryKind.Invoice ? InvoicesPage(2, 3) : null);

        InvokeNonPublic(table, "OnParametersSet");
    }

    [Fact]
    public async Task HistoryTable_ClampsPageRequestsAndSuppressesTheCurrentPage()
    {
        var requestedPages = new List<int>();
        var table = CreateHistoryTable(CustomerHistoryKind.Order, OrdersPage(2, 3), null, null);
        SetComponentParameters(table, new Dictionary<string, object?>
        {
            [nameof(CustomerHistoryTable.PageChanged)] = EventCallback.Factory.Create<int>(this, (int page) => requestedPages.Add(page)),
        });
        InvokeNonPublic(table, "OnParametersSet");

        await InvokeTaskAsync(table, "RequestPageAsync", 99);
        await InvokeTaskAsync(table, "RequestPageAsync", 2);
        await InvokeTaskAsync(table, "RequestPageAsync", -10);
        await InvokeTaskAsync(table, "RequestPageAsync", 2);

        Assert.Equal([3, 1], requestedPages);
    }

    [Theory]
    [InlineData(CustomerHistoryKind.Order, 41, "/Orders/View?id=41")]
    [InlineData(CustomerHistoryKind.Quotation, 42, "/Quotations/View?id=42")]
    [InlineData(CustomerHistoryKind.Invoice, 43, "/Invoices/View?id=43")]
    public void Activity_UsesTheRecordFamilySpecificDestination(CustomerHistoryKind kind, int id, string expected)
    {
        var component = CreateActivity();
        var item = new CustomerActivityItem(
            kind,
            id,
            kind == CustomerHistoryKind.Invoice ? "INV-43" : null,
            CustomerActivityStatus.Open,
            null,
            null,
            null,
            null,
            new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(expected, InvokeNonPublic(component, "RecordHref", item));
    }

    [Fact]
    public void Activity_RendersLoadingBeforeErrorAndDataThenErrorBeforeData()
    {
        var page = new CustomerActivityPage(
            [new CustomerActivityItem(
                CustomerHistoryKind.Order,
                41,
                null,
                CustomerActivityStatus.InProgress,
                1,
                2,
                null,
                null,
                new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc))],
            new CustomerHistorySourceSummary(CustomerHistorySourceState.Unavailable, null),
            new CustomerHistorySourceSummary(CustomerHistorySourceState.Available, 1),
            new CustomerHistorySourceSummary(CustomerHistorySourceState.Available, 1));

        var loading = CreateActivity();
        SetComponentParameters(loading, new Dictionary<string, object?>
        {
            [nameof(CustomerActivity.Page)] = page,
            [nameof(CustomerActivity.Loading)] = true,
            [nameof(CustomerActivity.Error)] = "ERROR-WINS-WHEN-NOT-LOADING",
        });
        var loadingComponents = RenderedComponentTypes(loading);
        Assert.Contains(typeof(MudProgressLinear), loadingComponents);
        Assert.DoesNotContain(typeof(MudAlert), loadingComponents);
        Assert.DoesNotContain(typeof(Legacy.Maliev.Intranet.Client.Shared.Components.LegacyLink), loadingComponents);

        var error = CreateActivity();
        SetComponentParameters(error, new Dictionary<string, object?>
        {
            [nameof(CustomerActivity.Page)] = page,
            [nameof(CustomerActivity.Loading)] = false,
            [nameof(CustomerActivity.Error)] = "ERROR-WINS-WHEN-NOT-LOADING",
        });
        var errorComponents = RenderedComponentTypes(error);
        Assert.Contains(typeof(MudAlert), errorComponents);
        Assert.DoesNotContain(typeof(MudProgressLinear), errorComponents);
        Assert.DoesNotContain(typeof(Legacy.Maliev.Intranet.Client.Shared.Components.LegacyLink), errorComponents);
    }

    [Fact]
    public void Activity_OmitsForbiddenSourcesAndOnlyWarnsAboutVisibleAuthorizedFailures()
    {
        var activity = CreateActivity();
        SetComponentParameters(activity, new Dictionary<string, object?>
        {
            [nameof(CustomerActivity.Page)] = new CustomerActivityPage(
                [],
                new CustomerHistorySourceSummary(CustomerHistorySourceState.Forbidden, null),
                new CustomerHistorySourceSummary(CustomerHistorySourceState.Unavailable, null),
                new CustomerHistorySourceSummary(CustomerHistorySourceState.Available, 0)),
        });

        var rendered = RenderedComponentTypes(activity);

        Assert.Single(rendered, type => type == typeof(MudAlert));
    }

    [Fact]
    public void CustomerLoadGate_CancelsAndRejectsAStaleCustomerResponse()
    {
        using var gate = new CustomerLoadGate();
        var customerA = gate.Begin(41);
        var customerB = gate.Begin(42);

        Assert.True(customerA.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(customerA));
        Assert.True(gate.IsCurrent(customerB));
        Assert.Equal(42, customerB.CustomerId);
    }

    [Fact]
    public async Task CustomerView_SlowPreviousRouteCannotOverwriteTheCurrentCustomer()
    {
        var handler = new CustomerRaceHandler();
        var view = new CustomerView { Id = 41 };
        SetNonPublicProperty(view, "Http", new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        SetNonPublicProperty(view, "AuthenticationStateProvider", new AuthenticatedStateProvider());

        var customerA = InvokeTaskAsync(view, "LoadAsync");
        await handler.CustomerAStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        view.Id = 42;
        var customerB = InvokeTaskAsync(view, "LoadAsync");
        await customerB;
        handler.ReleaseCustomerA.TrySetResult();
        await customerA;

        var loaded = Assert.IsType<CustomerDetail>(GetNonPublicField(view, "customer"));
        Assert.Equal(42, loaded.Id);
        view.Dispose();
    }

    [Fact]
    public async Task CustomerView_UsesSharedPermissionsWithoutExposingUnauthorizedTabs()
    {
        var owner = await LoadCustomerViewAsync(["platform.owner"]);
        var ownerTabs = Assert.IsType<List<string>>(InvokeNonPublic(owner, "VisibleTabs"));
        Assert.Equal(["overview", "activity", "orders", "quotations", "invoices"], ownerTabs);

        var scoped = await LoadCustomerViewAsync(
            ["legacy-customer.customers.read", "LEGACY.ORDERS.*"]);
        var scopedTabs = Assert.IsType<List<string>>(InvokeNonPublic(scoped, "VisibleTabs"));
        Assert.Equal(["overview", "activity", "orders"], scopedTabs);

        owner.Dispose();
        scoped.Dispose();
    }

    public static TheoryData<CustomerHistoryKind, bool, string?, OrderListPage?, QuotationListPage?, InvoiceListPage?> InvalidPageContracts =>
        new()
        {
            { CustomerHistoryKind.Order, false, null, null, null, null },
            { CustomerHistoryKind.Order, true, null, null, null, null },
            { CustomerHistoryKind.Order, false, "load failed", null, null, null },
            { CustomerHistoryKind.Order, false, null, null, QuotationsPage(1, 1), null },
            { CustomerHistoryKind.Order, false, null, OrdersPage(1, 1), QuotationsPage(1, 1), null },
            { CustomerHistoryKind.Quotation, false, null, null, null, InvoicesPage(1, 1) },
            { CustomerHistoryKind.Invoice, false, null, OrdersPage(1, 1), null, InvoicesPage(1, 1) },
        };

    private static CustomerHistoryTable CreateHistoryTable(
        CustomerHistoryKind kind,
        OrderListPage? orders,
        QuotationListPage? quotations,
        InvoiceListPage? invoices)
    {
        var table = new CustomerHistoryTable();
        SetComponentParameters(table, new Dictionary<string, object?>
        {
            [nameof(CustomerHistoryTable.Kind)] = kind,
            [nameof(CustomerHistoryTable.Orders)] = orders,
            [nameof(CustomerHistoryTable.Quotations)] = quotations,
            [nameof(CustomerHistoryTable.Invoices)] = invoices,
            [nameof(CustomerHistoryTable.Localize)] = (Func<string, string>)(key => key),
            [nameof(CustomerHistoryTable.LocalizeFormat)] = (Func<string, object?[], string>)((key, values) => $"{key}:{string.Join('|', values)}"),
            [nameof(CustomerHistoryTable.DisplayValue)] = (Func<string?, string>)(value => value ?? "N/A"),
            [nameof(CustomerHistoryTable.FormatDate)] = (Func<DateTime?, string>)(value => value?.ToString("O") ?? "N/A"),
            [nameof(CustomerHistoryTable.FormatMoney)] = (Func<decimal?, string, string>)((amount, currency) => $"{amount:N2} {currency}"),
        });
        return table;
    }

    private static CustomerActivity CreateActivity()
    {
        var activity = new CustomerActivity();
        SetComponentParameters(activity, new Dictionary<string, object?>
        {
            [nameof(CustomerActivity.Localize)] = (Func<string, string>)(key => key),
            [nameof(CustomerActivity.LocalizeFormat)] = (Func<string, object?[], string>)((key, values) => $"{key}:{string.Join('|', values)}"),
            [nameof(CustomerActivity.FormatTimestamp)] = (Func<DateTime, string>)(value => value.ToString("O")),
            [nameof(CustomerActivity.FormatMoney)] = (Func<decimal, string, string>)((amount, currency) => $"{amount:N2} {currency}"),
        });
        return activity;
    }

    private static OrderListPage OrdersPage(int pageIndex, int totalPages) =>
        new([], pageIndex, totalPages, 0, pageIndex < totalPages, pageIndex > 1);

    private static QuotationListPage QuotationsPage(int pageIndex, int totalPages) =>
        new([], pageIndex, totalPages, 0, pageIndex < totalPages, pageIndex > 1);

    private static InvoiceListPage InvoicesPage(int pageIndex, int totalPages) =>
        new([], pageIndex, totalPages, 0, pageIndex < totalPages, pageIndex > 1);

    private static object? InvokeNonPublic(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method.Invoke(target, arguments);
    }

    private static async Task InvokeTaskAsync(object target, string methodName, params object?[] arguments) =>
        await Assert.IsAssignableFrom<Task>(InvokeNonPublic(target, methodName, arguments));

    private static void SetComponentParameters(object component, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(component, value);
        }
    }

    private static void SetNonPublicProperty(object target, string name, object value) =>
        (target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, name)).SetValue(target, value);

    private static object? GetNonPublicField(object target, string name) =>
        (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name)).GetValue(target);

    private static async Task<CustomerView> LoadCustomerViewAsync(string[] grants)
    {
        var view = new CustomerView { Id = 42 };
        SetNonPublicProperty(view, "Http", new HttpClient(new CustomerRaceHandler()) { BaseAddress = new Uri("https://localhost") });
        SetNonPublicProperty(view, "AuthenticationStateProvider", new AuthenticatedStateProvider(grants));
        await InvokeTaskAsync(view, "LoadAsync");
        return view;
    }

    private static IReadOnlyList<Type> RenderedComponentTypes(ComponentBase component)
    {
        var builder = new RenderTreeBuilder();
        InvokeNonPublic(component, "BuildRenderTree", builder);
        var frames = builder.GetFrames();
        return frames.Array
            .Take(frames.Count)
            .Where(frame => frame.FrameType == RenderTreeFrameType.Component)
            .Select(frame => frame.ComponentType)
            .OfType<Type>()
            .ToArray();
    }

    private sealed class AuthenticatedStateProvider : Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider
    {
        private readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationState state;

        public AuthenticatedStateProvider(params string[] grants)
        {
            grants = grants.Length == 0 ? ["legacy-customer.customers.read"] : grants;
            state = new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(
                    grants.Select(grant => new Claim("permissions", grant)),
                    "test")));
        }

        public override Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(state);
    }

    private sealed class CustomerRaceHandler : HttpMessageHandler
    {
        public TaskCompletionSource CustomerAStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCustomerA { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var id = request.RequestUri!.AbsolutePath.EndsWith("/41", StringComparison.Ordinal) ? 41 : 42;
            if (id == 41)
            {
                CustomerAStarted.TrySetResult();
                await ReleaseCustomerA.Task;
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CustomerDetail(
                    id,
                    $"Customer{id}",
                    "Test",
                    $"Customer{id} Test",
                    null,
                    null,
                    null,
                    $"customer{id}@example.com",
                    null,
                    null,
                    null,
                    null,
                    DateTime.UtcNow,
                    null,
                    null,
                    null,
                    null)),
            };
        }
    }
}
