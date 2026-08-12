extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using InvoicesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoicesProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;
using QuotationsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationsProxy;
using CustomerActivityAggregator = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerActivityAggregator;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffCustomerHistoryContractTests
{
    [Fact]
    public async Task Activity_AnonymousEmployee_IsUnauthorizedBeforeDownstream()
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            [
                LegacyEmployeePermissions.CustomersRead,
                LegacyEmployeePermissions.OrdersRead,
                LegacyEmployeePermissions.QuotationsRead,
                LegacyEmployeePermissions.AccountingRead,
            ]);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/customers/42/activity");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, orders.RequestCount);
        Assert.Equal(0, quotations.RequestCount);
        Assert.Equal(0, invoices.RequestCount);
    }

    [Fact]
    public async Task Activity_MissingCustomerRead_IsForbiddenBeforeDownstream()
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            [
                LegacyEmployeePermissions.OrdersRead,
                LegacyEmployeePermissions.QuotationsRead,
                LegacyEmployeePermissions.AccountingRead,
            ]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, orders.RequestCount);
        Assert.Equal(0, quotations.RequestCount);
        Assert.Equal(0, invoices.RequestCount);
    }

    [Fact]
    public async Task Activity_CallsOnlyExactAuthorizedFamilies_AndMarksOthersForbidden()
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            [LegacyEmployeePermissions.CustomersRead, LegacyEmployeePermissions.OrdersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity?size=7");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Available, page.Orders.State);
        Assert.Equal(1, page.Orders.TotalRecords);
        Assert.Equal(CustomerHistorySourceState.Forbidden, page.Quotations.State);
        Assert.Null(page.Quotations.TotalRecords);
        Assert.Equal(CustomerHistorySourceState.Forbidden, page.Invoices.State);
        Assert.Null(page.Invoices.TotalRecords);
        Assert.Equal(
            [
                "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=&index=1&size=7",
                "/Orders/customers/42?sort=OrderModifiedDate_Descending&search=&index=1&size=7",
            ],
            orders.Paths.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(0, quotations.RequestCount);
        Assert.Equal(0, invoices.RequestCount);
    }

    [Fact]
    public async Task Activity_MapsTypedStatusesAndFields_WithDeterministicNewestFirstOrdering()
    {
        var orders = new RecordingHandler(ActivityOrdersJson());
        var quotations = new RecordingHandler(ActivityQuotationsJson());
        var invoices = new RecordingHandler(ActivityInvoicesJson());
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity?size=20");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(3, page.Orders.TotalRecords);
        Assert.Equal(4, page.Quotations.TotalRecords);
        Assert.Equal(3, page.Invoices.TotalRecords);
        Assert.Equal(
            [
                (CustomerHistoryKind.Order, 90),
                (CustomerHistoryKind.Order, 84),
                (CustomerHistoryKind.Quotation, 9),
                (CustomerHistoryKind.Quotation, 8),
                (CustomerHistoryKind.Invoice, 12),
                (CustomerHistoryKind.Invoice, 11),
                (CustomerHistoryKind.Quotation, 6),
            ],
            page.Items.Select(item => (item.Kind, item.Id)).ToArray());

        var completedOrder = page.Items.Single(item => item.Kind == CustomerHistoryKind.Order && item.Id == 90);
        Assert.Equal("completed-order", completedOrder.Label);
        Assert.Equal(CustomerActivityStatus.Complete, completedOrder.Status);
        Assert.Equal(4, completedOrder.CompletedUnits);
        Assert.Equal(4, completedOrder.TotalUnits);
        Assert.Null(completedOrder.Amount);
        Assert.Null(completedOrder.Currency);
        Assert.Equal(new DateTime(2030, 7, 20, 12, 0, 0, DateTimeKind.Utc), completedOrder.Timestamp);

        var openQuotation = page.Items.Single(item => item.Kind == CustomerHistoryKind.Quotation && item.Id == 9);
        Assert.Null(openQuotation.Label);
        Assert.Equal(CustomerActivityStatus.Open, openQuotation.Status);
        Assert.Null(openQuotation.CompletedUnits);
        Assert.Null(openQuotation.TotalUnits);
        Assert.Null(openQuotation.Amount);
        Assert.Null(openQuotation.Currency);

        var acceptedQuotation = page.Items.Single(item => item.Kind == CustomerHistoryKind.Quotation && item.Id == 8);
        Assert.Equal(CustomerActivityStatus.Accepted, acceptedQuotation.Status);

        var declinedQuotation = page.Items.Single(item => item.Kind == CustomerHistoryKind.Quotation && item.Id == 6);
        Assert.Equal(CustomerActivityStatus.Declined, declinedQuotation.Status);

        var paidInvoice = page.Items.Single(item => item.Kind == CustomerHistoryKind.Invoice && item.Id == 12);
        Assert.Equal("INV-12", paidInvoice.Label);
        Assert.Equal(CustomerActivityStatus.Paid, paidInvoice.Status);
        Assert.Equal(1200m, paidInvoice.Amount);
        Assert.Equal("THB", paidInvoice.Currency);
        Assert.Equal(new DateTime(2030, 7, 20, 12, 0, 0, DateTimeKind.Utc), paidInvoice.Timestamp);

        var outstandingInvoice = page.Items.Single(item => item.Kind == CustomerHistoryKind.Invoice && item.Id == 11);
        Assert.Equal(CustomerActivityStatus.Outstanding, outstandingInvoice.Status);
        Assert.Equal(1100m, outstandingInvoice.Amount);
        Assert.Equal("USD", outstandingInvoice.Currency);

        Assert.DoesNotContain(page.Items, item => item.Id is 85 or 7 or 10);
    }

    [Fact]
    public async Task Activity_UnionsCreationAndEventSorts_SoOlderCreatedRecentActivityIsIncluded()
    {
        var orders = new RecordingHandler("unused")
        {
            ResponseBody = request => request.RequestUri!.Query.Contains("OrderModifiedDate_Descending", StringComparison.Ordinal)
                ? OrderPageJson(42, 41, "old-order-recently-modified", "2029-01-01T00:00:00Z", "2031-01-03T00:00:00Z")
                : OrderPageJson(42, 99, "new-order-not-recent", "2030-01-01T00:00:00Z", null),
        };
        var quotations = new RecordingHandler("unused")
        {
            ResponseBody = request => request.RequestUri!.Query.Contains("QuotationModifiedDate_Descending", StringComparison.Ordinal)
                ? QuotationPageJson(42, 42, "2029-01-01T00:00:00Z", "2031-01-02T00:00:00Z")
                : QuotationPageJson(42, 98, "2030-01-01T00:00:00Z", null),
        };
        var invoices = new RecordingHandler("unused")
        {
            ResponseBody = request => request.RequestUri!.Query.Contains("InvoicePaymentDate_Descending", StringComparison.Ordinal)
                ? InvoicePageJson(42, 43, "INV-43", "2029-01-01T00:00:00Z", "2031-01-01T00:00:00Z", true)
                : InvoicePageJson(42, 97, "INV-97", "2030-01-01T00:00:00Z", null, false),
        };
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity?size=3");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(
            [
                (CustomerHistoryKind.Order, 41),
                (CustomerHistoryKind.Quotation, 42),
                (CustomerHistoryKind.Invoice, 43),
            ],
            page.Items.Select(item => (item.Kind, item.Id)).ToArray());
        Assert.Equal(2, orders.RequestCount);
        Assert.Equal(2, quotations.RequestCount);
        Assert.Equal(2, invoices.RequestCount);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task Activity_NullItem_IsInvalidResponseWithoutPageWideFailure(string family)
    {
        var orders = new RecordingHandler(family == "orders" ? NullItemPageJson() : PageJson("orders", 42));
        var quotations = new RecordingHandler(family == "quotations" ? NullItemPageJson() : PageJson("quotations", 42));
        var invoices = new RecordingHandler(family == "invoices" ? NullItemPageJson() : PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.InvalidResponse, SummaryFor(page, family).State);
        Assert.Contains(page.Items, item => item.Kind != KindFor(family));
        Assert.DoesNotContain(page.Items, item => item.Kind == KindFor(family));
    }

    [Theory]
    [InlineData("rate-limited", CustomerHistorySourceState.RateLimited)]
    [InlineData("invalid", CustomerHistorySourceState.InvalidResponse)]
    [InlineData("unavailable", CustomerHistorySourceState.Unavailable)]
    public async Task Activity_OneValidPairedPage_RetainsItemsButDoesNotClaimAvailable(
        string secondaryFailure,
        CustomerHistorySourceState expectedState)
    {
        var orders = new RecordingHandler("unused")
        {
            Response = request => request.RequestUri!.Query.Contains("OrderCreatedDate_Descending", StringComparison.Ordinal)
                ? JsonResponse(PageJson("orders", 42))
                : FailureResponse(secondaryFailure),
        };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(expectedState, page.Orders.State);
        Assert.Null(page.Orders.TotalRecords);
        Assert.Contains(page.Items, item => item.Kind == CustomerHistoryKind.Order && item.Id == 84);
    }

    [Fact]
    public async Task Activity_OneForbiddenPairedPage_DiscardsFamilyItemsFromTheOtherLeg()
    {
        var orders = new RecordingHandler("unused")
        {
            Response = request => request.RequestUri!.Query.Contains("OrderCreatedDate_Descending", StringComparison.Ordinal)
                ? JsonResponse(PageJson("orders", 42))
                : FailureResponse("forbidden"),
        };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Forbidden, page.Orders.State);
        Assert.DoesNotContain(page.Items, item => item.Kind == CustomerHistoryKind.Order);
    }

    [Theory]
    [InlineData("unavailable", "invalid", CustomerHistorySourceState.InvalidResponse)]
    [InlineData("invalid", "rate-limited", CustomerHistorySourceState.RateLimited)]
    [InlineData("rate-limited", "forbidden", CustomerHistorySourceState.Forbidden)]
    public async Task Activity_PairedFailuresUseDeterministicPrecedence(
        string creationFailure,
        string alternateFailure,
        CustomerHistorySourceState expectedState)
    {
        var orders = new RecordingHandler("unused")
        {
            Response = request => request.RequestUri!.Query.Contains("OrderCreatedDate_Descending", StringComparison.Ordinal)
                ? FailureResponse(creationFailure)
                : FailureResponse(alternateFailure),
        };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(expectedState, page.Orders.State);
        Assert.DoesNotContain(page.Items, item => item.Kind == CustomerHistoryKind.Order);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(999, 50)]
    public async Task Activity_ClampsRequestedSizeForEveryAuthorizedSource(int requested, int expected)
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/activity?size={requested}");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.True(page.Items.Count <= expected);
        Assert.Equal(2, orders.RequestCount);
        Assert.Equal(2, quotations.RequestCount);
        Assert.Equal(2, invoices.RequestCount);
        Assert.All(orders.Paths, path => Assert.EndsWith($"&size={expected}", path, StringComparison.Ordinal));
        Assert.All(quotations.Paths, path => Assert.EndsWith($"&size={expected}", path, StringComparison.Ordinal));
        Assert.All(invoices.Paths, path => Assert.EndsWith($"&size={expected}", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activity_AuthorizedSourceFailures_ReturnPartialSuccessWithoutPayloadLeak()
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler("quotation-rate-limit-secret")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 3,
        };
        var invoices = new RecordingHandler("invoice-transport-secret")
        {
            Exception = new HttpRequestException("invoice-transport-secret"),
        };
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<CustomerActivityPage>(body, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(CustomerHistoryKind.Order, page.Items[0].Kind);
        Assert.Equal(CustomerHistorySourceState.Available, page.Orders.State);
        Assert.Equal(CustomerHistorySourceState.RateLimited, page.Quotations.State);
        Assert.Null(page.Quotations.TotalRecords);
        Assert.Equal(CustomerHistorySourceState.Unavailable, page.Invoices.State);
        Assert.Null(page.Invoices.TotalRecords);
        Assert.DoesNotContain("quotation-rate-limit-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice-transport-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activity_StartsAuthorizedSourceRequestsConcurrently()
    {
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async Task WaitForOtherSourcesAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref started) == 6)
            {
                allStarted.TrySetResult();
            }

            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }

        var orders = new RecordingHandler(PageJson("orders", 42)) { BeforeResponseAsync = WaitForOtherSourcesAsync };
        var quotations = new RecordingHandler(PageJson("quotations", 42)) { BeforeResponseAsync = WaitForOtherSourcesAsync };
        var invoices = new RecordingHandler(PageJson("invoices", 42)) { BeforeResponseAsync = WaitForOtherSourcesAsync };
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, started);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CustomerHistorySourceState.Available, 0)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerHistorySourceState.Unavailable, null)]
    public async Task Activity_HttpBoundaryStatus_IsEncodedPerSource(
        HttpStatusCode statusCode,
        CustomerHistorySourceState expectedState,
        int? expectedTotal)
    {
        var orders = new RecordingHandler("unused") { StatusCode = statusCode };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(expectedState, page.Orders.State);
        Assert.Equal(expectedTotal, page.Orders.TotalRecords);
    }

    [Theory]
    [InlineData("cancellation")]
    [InlineData("timeout")]
    public async Task Activity_InternalCancellationAndTimeout_AreUnavailable(string failure)
    {
        var orders = new RecordingHandler("unused")
        {
            Exception = failure == "cancellation"
                ? new TaskCanceledException("internal-cancellation")
                : new Polly.Timeout.TimeoutRejectedException("internal-timeout"),
        };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(orders, quotations, invoices, AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var page = await response.Content.ReadFromJsonAsync<CustomerActivityPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Unavailable, page.Orders.State);
    }

    [Fact]
    public async Task Activity_CallerCancellation_IsPropagated()
    {
        var never = new RecordingHandler("unused")
        {
            BeforeResponseAsync = token => Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        var tokenProvider = new HistoryServiceTokenProvider();
        var aggregator = new CustomerActivityAggregator(
            new OrdersProxy(CreateDownstreamClient(never, tokenProvider, "http://order/")),
            new QuotationsProxy(CreateDownstreamClient(never, tokenProvider, "http://quotation/")),
            new InvoicesProxy(CreateDownstreamClient(never, tokenProvider, "http://accounting/")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            AllActivityPermissions.Select(permission => new System.Security.Claims.Claim("permissions", permission)),
            "test"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => aggregator.GetAsync(42, 20, user, cancellation.Token));
    }

    [Fact]
    public async Task Activity_BodyReadFailure_IsUnavailableWithoutPayloadLeak()
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler("unused")
        {
            BodyReadException = new HttpRequestException("activity-body-read-secret"),
        };
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<CustomerActivityPage>(body, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Unavailable, page.Quotations.State);
        Assert.DoesNotContain("activity-body-read-secret", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("wrong-owner")]
    public async Task Activity_InvalidAuthorizedSource_IsEncodedWithoutPayloadLeak(string failure)
    {
        var orders = new RecordingHandler(PageJson("orders", 42));
        var quotations = new RecordingHandler(failure == "invalid-json"
            ? "quotation-invalid-secret"
            : PageJson("quotations", 41, "quotation-owner-secret"));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<CustomerActivityPage>(body, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Available, page.Orders.State);
        Assert.Equal(CustomerHistorySourceState.InvalidResponse, page.Quotations.State);
        Assert.Equal(CustomerHistorySourceState.Available, page.Invoices.State);
        Assert.DoesNotContain("quotation-invalid-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("quotation-owner-secret", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Activity_DownstreamAuthorizationFailure_IsEncodedAsForbidden(HttpStatusCode statusCode)
    {
        var orders = new RecordingHandler("downstream-auth-secret") { StatusCode = statusCode };
        var quotations = new RecordingHandler(PageJson("quotations", 42));
        var invoices = new RecordingHandler(PageJson("invoices", 42));
        await using var factory = new CustomerActivityBffFactory(
            orders,
            quotations,
            invoices,
            AllActivityPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/activity");
        var body = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<CustomerActivityPage>(body, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(CustomerHistorySourceState.Forbidden, page.Orders.State);
        Assert.DoesNotContain("downstream-auth-secret", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orders", LegacyEmployeePermissions.OrdersRead, "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=&index=1&size=100")]
    [InlineData("quotations", LegacyEmployeePermissions.QuotationsRead, "/quotations/customers/42?sort=QuotationCreatedDate_Descending&search=&index=1&size=100")]
    [InlineData("invoices", LegacyEmployeePermissions.AccountingRead, "/invoices/customers/42?sort=InvoiceCreatedDate_Descending&search=&index=1&size=100")]
    public async Task CustomerFamily_ForwardsExplicitOwnerRoute(
        string family,
        string permission,
        string expectedPath)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [permission]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}?index=-3&size=999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedPath, downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
    }

    [Fact]
    public async Task CustomerOrders_PreserveServiceTimestampsInBrowserSafeProjection()
    {
        var downstream = new RecordingHandler(PageJson("orders", 42));
        await using var factory = new CustomerHistoryBffFactory(
            "orders",
            downstream,
            [LegacyEmployeePermissions.OrdersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2030-07-15T00:00:00", body.GetProperty("items")[0].GetProperty("createdDate").GetString());
        Assert.Equal("2030-07-16T00:00:00", body.GetProperty("items")[0].GetProperty("modifiedDate").GetString());
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task AnonymousCustomerFamily_IsUnauthorizedBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task MissingExactFamilyPermission_IsForbiddenBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(
            family,
            downstream,
            [LegacyEmployeePermissions.CustomersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task InvalidCustomerId_IsRejectedBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/0/{family}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task MismatchedCustomerItem_IsBadGatewayWithoutPayloadLeak(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 41, "owner-secret"));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("owner-secret", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task InvalidPayload_IsBadGatewayWithoutPayloadLeak(string family)
    {
        var downstream = new RecordingHandler("history-secret-not-json");
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("history-secret-not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orders", HttpStatusCode.Unauthorized)]
    [InlineData("orders", HttpStatusCode.Forbidden)]
    [InlineData("quotations", HttpStatusCode.Unauthorized)]
    [InlineData("quotations", HttpStatusCode.Forbidden)]
    [InlineData("invoices", HttpStatusCode.Unauthorized)]
    [InlineData("invoices", HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(string family, HttpStatusCode statusCode)
    {
        var downstream = new RecordingHandler("{}") { StatusCode = statusCode };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task NotFound_BecomesEmptyPageForRequestedIndex(string family)
    {
        var downstream = new RecordingHandler("{}") { StatusCode = HttpStatusCode.NotFound };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}?index=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, page.GetProperty("pageIndex").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.True(page.GetProperty("hasPreviousPage").GetBoolean());
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task RateLimit_PreservesBoundedRetryAfterWithoutPayload(string family)
    {
        var downstream = new RecordingHandler("rate-limit-secret")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 2,
        };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.DoesNotContain("rate-limit-secret", body, StringComparison.Ordinal);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Theory]
    [InlineData("orders", false)]
    [InlineData("orders", true)]
    [InlineData("quotations", false)]
    [InlineData("quotations", true)]
    [InlineData("invoices", false)]
    [InlineData("invoices", true)]
    public async Task TransportAndTimeoutFailures_AreServiceUnavailable(string family, bool timeout)
    {
        var downstream = new RecordingHandler("{}")
        {
            Exception = timeout
                ? new TaskCanceledException("history timeout")
                : new HttpRequestException("history unavailable"),
        };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("orders", "transport")]
    [InlineData("orders", "cancellation")]
    [InlineData("orders", "timeout")]
    [InlineData("quotations", "transport")]
    [InlineData("quotations", "cancellation")]
    [InlineData("quotations", "timeout")]
    [InlineData("invoices", "transport")]
    [InlineData("invoices", "cancellation")]
    [InlineData("invoices", "timeout")]
    public async Task BodyReadFailure_IsServiceUnavailableWithoutPayloadLeak(
        string family,
        string failure)
    {
        Exception exception = failure switch
        {
            "transport" => new HttpRequestException("body-read-transport-secret"),
            "cancellation" => new TaskCanceledException("body-read-cancellation-secret"),
            "timeout" => new Polly.Timeout.TimeoutRejectedException("body-read-timeout-secret"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var downstream = new RecordingHandler("unused") { BodyReadException = exception };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("body-read-", body, StringComparison.Ordinal);
    }

    private static string PermissionFor(string family) => family switch
    {
        "orders" => LegacyEmployeePermissions.OrdersRead,
        "quotations" => LegacyEmployeePermissions.QuotationsRead,
        "invoices" => LegacyEmployeePermissions.AccountingRead,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] AllActivityPermissions =
    [
        LegacyEmployeePermissions.CustomersRead,
        LegacyEmployeePermissions.OrdersRead,
        LegacyEmployeePermissions.QuotationsRead,
        LegacyEmployeePermissions.AccountingRead,
    ];

    private static CustomerHistoryKind KindFor(string family) => family switch
    {
        "orders" => CustomerHistoryKind.Order,
        "quotations" => CustomerHistoryKind.Quotation,
        "invoices" => CustomerHistoryKind.Invoice,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static CustomerHistorySourceSummary SummaryFor(CustomerActivityPage page, string family) => family switch
    {
        "orders" => page.Orders,
        "quotations" => page.Quotations,
        "invoices" => page.Invoices,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static string NullItemPageJson() =>
        """{"Items":[null],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";

    private static string OrderPageJson(
        int customerId,
        int id,
        string name,
        string? createdDate,
        string? modifiedDate) =>
        $$"""{"Items":[{"Id":{{id}},"CustomerId":{{customerId}},"EmployeeId":7,"Name":"{{name}}","ProcessId":3,"Quantity":2,"Manufactured":1,"Remaining":1,"Subtotal":225,"PromisedDate":null,"AllowSocialMedia":false,"CreatedDate":{{JsonDate(createdDate)}},"ModifiedDate":{{JsonDate(modifiedDate)}}}],"PageIndex":1,"TotalPages":1,"TotalRecords":2,"HasNextPage":false,"HasPreviousPage":false}""";

    private static string QuotationPageJson(
        int customerId,
        int id,
        string? createdDate,
        string? modifiedDate) =>
        $$"""{"Items":[{"Id":{{id}},"CustomerId":{{customerId}},"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2031-08-01T00:00:00Z","Subtotal":1000,"Vat":70,"Total":1070,"WithholdingTax":null,"QuotedAmount":1070,"CurrencyId":1,"Comment":null,"Fob":null,"ShippedVia":null,"Terms":null,"Accepted":null,"CreatedDate":{{JsonDate(createdDate)}},"ModifiedDate":{{JsonDate(modifiedDate)}}}],"PageIndex":1,"TotalPages":1,"TotalRecords":2,"HasNextPage":false,"HasPreviousPage":false}""";

    private static string InvoicePageJson(
        int customerId,
        int id,
        string number,
        string? createdDate,
        string? paymentDate,
        bool paid) =>
        $$"""{"Items":[{"Id":{{id}},"CustomerId":{{customerId}},"Number":"{{number}}","Currency":"THB","PurchaseOrderNumber":null,"Subtotal":1000,"Vat":70,"Total":1070,"WithholdingTax":null,"Outstanding":{{(paid ? 0 : 1070)}},"IsPaid":{{paid.ToString().ToLowerInvariant()}},"ReceiptId":null,"PaymentDate":{{JsonDate(paymentDate)}},"CreatedDate":{{JsonDate(createdDate)}}}],"PageIndex":1,"TotalPages":1,"TotalRecords":2,"HasNextPage":false,"HasPreviousPage":false}""";

    private static string JsonDate(string? value) => value is null ? "null" : $"\"{value}\"";

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage FailureResponse(string failure) => failure switch
    {
        "rate-limited" => JsonResponse("rate-limited-secret", HttpStatusCode.TooManyRequests),
        "invalid" => JsonResponse("invalid-secret"),
        "unavailable" => JsonResponse("unavailable-secret", HttpStatusCode.ServiceUnavailable),
        "forbidden" => JsonResponse("forbidden-secret", HttpStatusCode.Forbidden),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    private static string ActivityOrdersJson() =>
        """
        {"Items":[
          {"Id":84,"CustomerId":42,"EmployeeId":7,"Name":"active-order","ProcessId":3,"Quantity":4,"Manufactured":2,"Remaining":2,"Subtotal":800,"PromisedDate":null,"AllowSocialMedia":false,"CreatedDate":"2030-07-19T00:00:00Z","ModifiedDate":"2030-07-20T12:00:00Z"},
          {"Id":90,"CustomerId":42,"EmployeeId":7,"Name":"completed-order","ProcessId":3,"Quantity":4,"Manufactured":4,"Remaining":0,"Subtotal":900,"PromisedDate":null,"AllowSocialMedia":false,"CreatedDate":"2030-07-20T12:00:00Z","ModifiedDate":null},
          {"Id":85,"CustomerId":42,"EmployeeId":7,"Name":"missing-order-time","ProcessId":3,"Quantity":1,"Manufactured":0,"Remaining":1,"Subtotal":100,"PromisedDate":null,"AllowSocialMedia":false,"CreatedDate":null,"ModifiedDate":null}
        ],"PageIndex":1,"TotalPages":1,"TotalRecords":3,"HasNextPage":false,"HasPreviousPage":false}
        """;

    private static string ActivityQuotationsJson() =>
        """
        {"Items":[
          {"Id":9,"CustomerId":42,"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":900,"Vat":63,"Total":963,"WithholdingTax":null,"QuotedAmount":963,"CurrencyId":1,"Comment":null,"Fob":null,"ShippedVia":null,"Terms":null,"Accepted":null,"CreatedDate":"2030-07-20T12:00:00Z","ModifiedDate":null},
          {"Id":8,"CustomerId":42,"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":800,"Vat":56,"Total":856,"WithholdingTax":null,"QuotedAmount":856,"CurrencyId":1,"Comment":"accepted-comment","Fob":null,"ShippedVia":null,"Terms":null,"Accepted":true,"CreatedDate":"2030-07-20T12:00:00Z","ModifiedDate":null},
          {"Id":6,"CustomerId":42,"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":600,"Vat":42,"Total":642,"WithholdingTax":null,"QuotedAmount":642,"CurrencyId":1,"Comment":"declined-comment","Fob":null,"ShippedVia":null,"Terms":null,"Accepted":false,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null},
          {"Id":7,"CustomerId":42,"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":700,"Vat":49,"Total":749,"WithholdingTax":null,"QuotedAmount":749,"CurrencyId":1,"Comment":"missing-quotation-time","Fob":null,"ShippedVia":null,"Terms":null,"Accepted":false,"CreatedDate":null,"ModifiedDate":null}
        ],"PageIndex":1,"TotalPages":1,"TotalRecords":4,"HasNextPage":false,"HasPreviousPage":false}
        """;

    private static string ActivityInvoicesJson() =>
        """
        {"Items":[
          {"Id":12,"CustomerId":42,"Number":"INV-12","Currency":"THB","PurchaseOrderNumber":null,"Subtotal":1100,"Vat":100,"Total":1200,"WithholdingTax":null,"Outstanding":0,"IsPaid":true,"ReceiptId":3,"PaymentDate":"2030-07-20T12:00:00Z","CreatedDate":"2030-07-01T00:00:00Z"},
          {"Id":11,"CustomerId":42,"Number":"INV-11","Currency":"USD","PurchaseOrderNumber":null,"Subtotal":1000,"Vat":100,"Total":1100,"WithholdingTax":null,"Outstanding":1100,"IsPaid":false,"ReceiptId":null,"PaymentDate":null,"CreatedDate":"2030-07-20T12:00:00Z"},
          {"Id":10,"CustomerId":42,"Number":"INV-10","Currency":"THB","PurchaseOrderNumber":null,"Subtotal":900,"Vat":90,"Total":990,"WithholdingTax":null,"Outstanding":990,"IsPaid":false,"ReceiptId":null,"PaymentDate":null,"CreatedDate":null}
        ],"PageIndex":1,"TotalPages":1,"TotalRecords":3,"HasNextPage":false,"HasPreviousPage":false}
        """;

    private static string PageJson(string family, int customerId, string marker = "history-row") => family switch
    {
        "orders" => $$"""{"Items":[{"Id":84,"CustomerId":{{customerId}},"EmployeeId":7,"Name":"{{marker}}","ProcessId":3,"Quantity":2,"Manufactured":1,"Remaining":1,"Subtotal":225,"PromisedDate":"2030-07-20T00:00:00","AllowSocialMedia":false,"CreatedDate":"2030-07-15T00:00:00","ModifiedDate":"2030-07-16T00:00:00"}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        "quotations" => $$"""{"Items":[{"Id":7,"CustomerId":{{customerId}},"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"QuotedAmount":1040.27,"CurrencyId":1,"Comment":"{{marker}}","Fob":"Bangkok","ShippedVia":"Courier","Terms":"Net 7","Accepted":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        "invoices" => $$"""{"Items":[{"Id":7,"CustomerId":{{customerId}},"Number":"{{marker}}","Currency":"THB","PurchaseOrderNumber":"PO-7","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"Outstanding":1040.27,"IsPaid":false,"ReceiptId":null,"PaymentDate":null,"CreatedDate":"2030-07-18T00:00:00Z"}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static HttpClient CreateDownstreamClient(
        HttpMessageHandler downstream,
        IServiceAccessTokenProvider tokenProvider,
        string baseAddress)
    {
        var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
        return new HttpClient(authHandler)
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Customers/View?id=42",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class CustomerHistoryBffFactory(
        string family,
        RecordingHandler downstream,
        IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new HistoryAuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new HistoryServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                switch (family)
                {
                    case "orders":
                        services.RemoveAll<OrdersProxy>();
                        services.AddSingleton(new OrdersProxy(CreateDownstreamClient(downstream, tokenProvider, "http://order/")));
                        break;
                    case "quotations":
                        services.RemoveAll<QuotationsProxy>();
                        services.AddSingleton(new QuotationsProxy(CreateDownstreamClient(downstream, tokenProvider, "http://quotation/")));
                        break;
                    case "invoices":
                        services.RemoveAll<InvoicesProxy>();
                        services.AddSingleton(new InvoicesProxy(CreateDownstreamClient(downstream, tokenProvider, "http://accounting/")));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(family));
                }
            });
        }

        private static HttpClient CreateDownstreamClient(
            HttpMessageHandler downstream,
            IServiceAccessTokenProvider tokenProvider,
            string baseAddress)
        {
            var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
            return new HttpClient(authHandler)
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }
    }

    private sealed class CustomerActivityBffFactory(
        RecordingHandler orders,
        RecordingHandler quotations,
        RecordingHandler invoices,
        IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new HistoryAuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new HistoryServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                services.RemoveAll<OrdersProxy>();
                services.AddSingleton(new OrdersProxy(CreateDownstreamClient(orders, tokenProvider, "http://order/")));
                services.RemoveAll<QuotationsProxy>();
                services.AddSingleton(new QuotationsProxy(CreateDownstreamClient(quotations, tokenProvider, "http://quotation/")));
                services.RemoveAll<InvoicesProxy>();
                services.AddSingleton(new InvoicesProxy(CreateDownstreamClient(invoices, tokenProvider, "http://accounting/")));
            });
        }

        private static HttpClient CreateDownstreamClient(
            HttpMessageHandler downstream,
            IServiceAccessTokenProvider tokenProvider,
            string baseAddress)
        {
            var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
            return new HttpClient(authHandler)
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }
    }

    private sealed class HistoryServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class HistoryAuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(
                    "server-only-access-token",
                    "server-only-refresh-token",
                    "Bearer",
                    900,
                    DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, permissions, 7)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
            int databaseId,
            CreateCustomerIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);

        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(
            int databaseId,
            CreateEmployeeIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int? RetryAfterSeconds { get; set; }
        public Exception? Exception { get; set; }
        public Exception? BodyReadException { get; set; }
        public Func<CancellationToken, Task>? BeforeResponseAsync { get; set; }
        public Func<HttpRequestMessage, string>? ResponseBody { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? Response { get; set; }
        public string? PathAndQuery { get; private set; }
        public IReadOnlyList<string> Paths => paths.ToArray();
        public string? Authorization { get; private set; }
        public int RequestCount => requestCount;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> paths = new();
        private int requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            PathAndQuery = request.RequestUri?.PathAndQuery;
            paths.Enqueue(PathAndQuery ?? string.Empty);
            Authorization = request.Headers.Authorization?.ToString();
            if (BeforeResponseAsync is not null)
            {
                await BeforeResponseAsync(cancellationToken);
            }

            if (Exception is not null)
            {
                throw Exception;
            }

            if (Response is not null)
            {
                return Response(request);
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = BodyReadException is null
                    ? new StringContent(ResponseBody?.Invoke(request) ?? body, Encoding.UTF8, "application/json")
                    : new FaultingHttpContent(BodyReadException),
            };
            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            return response;
        }
    }

    private sealed class FaultingHttpContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
