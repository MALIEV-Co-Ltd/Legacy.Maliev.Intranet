using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Accounting.Components;
using Legacy.Maliev.Intranet.Client.Features.Accounting.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinanceEmptyLookupHttpTests
{
    public static IEnumerable<object[]> LookupCases()
    {
        foreach (var endpoint in new[] { "/bff/finances/create", "/bff/finances/84" })
            foreach (var resource in new[] { "directions", "types", "methods" })
                foreach (var status in new[] { 200, 404, 401, 403, 500 })
                    yield return [endpoint, resource, status];
    }

    [Theory]
    [MemberData(nameof(LookupCases))]
    public async Task Lookup_OnlySuccessfulEmptyOrNotFoundBecomesEmpty(string endpoint, string resource, int status)
    {
        using var accounting = Routes(resource, status);
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync(endpoint);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        if (status is 200 or 404)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, body.GetProperty(resource).GetArrayLength());
            foreach (var other in new[] { "directions", "types", "methods" }.Where(value => value != resource))
            {
                var item = Assert.Single(body.GetProperty(other).EnumerateArray());
                Assert.Equal(1, item.GetProperty("id").GetInt32());
                Assert.Equal("Existing lookup", item.GetProperty("name").GetString());
            }
            Assert.Single(body.GetProperty("employees").EnumerateArray());
            Assert.Single(body.GetProperty("currencies").EnumerateArray());
            if (endpoint.EndsWith("/84", StringComparison.Ordinal))
            {
                Assert.Equal(84, body.GetProperty("payment").GetProperty("id").GetInt32());
                Assert.Empty(body.GetProperty("files").EnumerateArray());
            }
        }
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Finance workflow unavailable", body.GetProperty("title").GetString());
            Assert.False(body.TryGetProperty(resource, out _));
        }
        AssertReadBoundaries(accounting, endpoint.EndsWith("/84", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/bff/finances/create")]
    [InlineData("/bff/finances/84")]
    public async Task Lookup_MalformedSuccessfulResponseRemainsUnavailable(string endpoint)
    {
        using var accounting = Routes("types", 200, malformed: true);
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertReadBoundaries(accounting, endpoint.EndsWith("/84", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingPaymentRemainsNotFoundAndMissingFilesRemainEmpty()
    {
        using var accounting = Routes("all", 404);
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var payment = await client.GetAsync("/bff/finances/85");
        using var detail = await client.GetAsync("/bff/finances/84");

        Assert.Equal(HttpStatusCode.NotFound, payment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var page = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(84, page.GetProperty("payment").GetProperty("id").GetInt32());
        Assert.Empty(page.GetProperty("files").EnumerateArray());
        Assert.All(accounting.Requests, request => Assert.Equal("GET", request.Method));
    }

    [Theory]
    [InlineData("/Finances/Create")]
    [InlineData("/Finances/View?id=84")]
    public async Task EmptyProducerLookupsThroughRealBffRenderExistingEditorWithoutInventedOptions(string route)
    {
        using var accounting = Routes("all", 404);
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(client);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        var cut = context.Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(FinanceCreate).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(data => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), data);
                builder.CloseComponent();
            })));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("form.finance-editor__form"));
            Assert.Empty(cut.FindAll("[role='alert']"));
            var lookupFields = cut.FindComponents<AccountingSelectField<int>>()
                .Where(field => field.Instance.Id is "finance-direction" or "finance-type" or "finance-method").ToArray();
            Assert.Equal(3, lookupFields.Length);
            Assert.All(lookupFields, field => Assert.Empty(field.Instance.Options));
            Assert.Single(cut.FindAll("#finance-amount"));
            if (route.Contains("View", StringComparison.Ordinal))
                Assert.Equal(1234.56m, Assert.Single(cut.FindComponents<AccountingInputField<decimal>>()).Instance.Value);
        }, TimeSpan.FromSeconds(15));
        AssertReadBoundaries(accounting, route.Contains("View", StringComparison.Ordinal));
    }

    private static AccountingBehaviorTestHost.RecordingRouteHandler Routes(string emptyResource, int status, bool malformed = false) =>
        AccountingBehaviorTestHost.Routes(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("The lookup consumer must not write.");
            if (path == "/payments/84") return AccountingBehaviorTestHost.Json(FinanceAccountingBehaviorTests.PaymentJson);
            if (path is "/payments/84/files" or "/payments/85" or "/payments/85/files") return new(HttpStatusCode.NotFound);
            if (path is "/payments/directions" or "/payments/types" or "/payments/methods")
            {
                if (emptyResource == "all" || path == $"/payments/{emptyResource}")
                    return status == 200
                        ? AccountingBehaviorTestHost.Json(malformed ? "{" : "[]")
                        : new((HttpStatusCode)status);
                // Accounting retains PascalCase serialization for nonempty lists.
                return AccountingBehaviorTestHost.Json("""[{"Id":1,"Name":"Existing lookup"}]""");
            }
            throw new InvalidOperationException("Unexpected lookup boundary.");
        });

    private static void AssertReadBoundaries(AccountingBehaviorTestHost.RecordingRouteHandler accounting, bool detail)
    {
        var expected = detail
            ? new[] { "/payments/84", "/payments/84/files", "/payments/directions", "/payments/methods", "/payments/types" }
            : ["/payments/directions", "/payments/methods", "/payments/types"];
        Assert.Equal(expected, accounting.Requests.Select(request => request.Path).Order(StringComparer.Ordinal));
        Assert.All(accounting.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Equal("Bearer signed-service-token", request.Authorization);
            Assert.Null(request.Body);
        });
    }
}
