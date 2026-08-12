using System.Reflection;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
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
}
