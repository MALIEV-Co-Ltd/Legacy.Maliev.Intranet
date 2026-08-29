using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using QuotationView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.Quotations.View;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationDecisionUiBehaviorTests
{
    [Fact]
    public async Task AuthorizedDecision_ConfirmsThenUsesFreshCsrfAndUpdatesPersistedState()
    {
        var handler = new DecisionUiHandler();
        var view = await CreateLoadedViewAsync(handler, LegacyQuotationPermissions.Update);

        Invoke(view, "BeginDecision", true);

        Assert.True(Field<bool>(view, "pendingDecision"));
        Assert.Equal(0, handler.DecisionCalls);

        await InvokeTaskAsync(view, "SubmitDecisionAsync");

        Assert.Equal(1, handler.SessionCallsAfterDetail);
        Assert.Equal(1, handler.DecisionCalls);
        Assert.Equal("fresh-csrf", handler.Csrf);
        using var body = System.Text.Json.JsonDocument.Parse(Assert.IsType<string>(handler.DecisionBody));
        Assert.Equal(["accepted", "expectedModifiedDate"], body.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.True(body.RootElement.GetProperty("accepted").GetBoolean());
        var page = Assert.IsType<QuotationDetailPage>(Field<object>(view, "page"));
        Assert.True(page.Quotation.Accepted);
        Assert.Equal("DecisionSucceeded", Field<string>(view, "decisionMessage"));
        Assert.Null(Field<object?>(view, "pendingDecision"));
        Assert.False(Field<bool>(view, "decisionBusy"));
    }

    [Fact]
    public async Task MissingPermission_HidesDecisionAndCannotEnterConfirmation()
    {
        var handler = new DecisionUiHandler();
        var view = await CreateLoadedViewAsync(handler);

        Assert.False(Field<bool>(view, "canDecide"));
        Invoke(view, "BeginDecision", true);

        Assert.Null(Field<object?>(view, "pendingDecision"));
        Assert.Equal(0, handler.DecisionCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "DependencyConflict", "DecisionPartial", 0, true)]
    [InlineData(HttpStatusCode.Conflict, null, "DecisionConflict", 0, true)]
    [InlineData(HttpStatusCode.TooManyRequests, null, "DecisionRateLimited", 17, true)]
    [InlineData(HttpStatusCode.Forbidden, null, "DecisionForbidden", 0, false)]
    [InlineData(HttpStatusCode.BadGateway, null, "DecisionInvalidResponse", 0, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, "DecisionUnavailable", 0, true)]
    public async Task FailedDecision_ProjectsRecoverableLocalizedState(
        HttpStatusCode status,
        string? downstreamDecisionStatus,
        string expectedError,
        int expectedRetryAfter,
        bool remainsAuthorized)
    {
        var handler = new DecisionUiHandler
        {
            DecisionStatus = status,
            DecisionResponseStatus = downstreamDecisionStatus,
        };
        var view = await CreateLoadedViewAsync(handler, LegacyQuotationPermissions.Update);
        Invoke(view, "BeginDecision", false);

        await InvokeTaskAsync(view, "SubmitDecisionAsync");

        Assert.Equal(expectedError, Field<string>(view, "decisionError"));
        Assert.Equal(expectedRetryAfter, Field<int>(view, "retryAfterSeconds"));
        Assert.Equal(remainsAuthorized, Field<bool>(view, "canDecide"));
        Assert.False(Field<bool>(view, "decisionBusy"));
        Assert.Null(Assert.IsType<QuotationDetailPage>(Field<object>(view, "page")).Quotation.Accepted);
    }

    private static async Task<QuotationView> CreateLoadedViewAsync(DecisionUiHandler handler, params string[] permissions)
    {
        var view = new QuotationView { Id = 84 };
        Set(view, "Http", new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        Set(view, "Navigation", new TestNavigationManager());
        Set(view, "Text", new KeyLocalizer<QuotationView>());
        Set(view, "AuthenticationStateProvider", new StateProvider(permissions));
        await InvokeTaskAsync(view, "LoadAsync");
        return view;
    }

    private static object? Invoke(object target, string name, params object?[] arguments) =>
        (target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, name)).Invoke(target, arguments);

    private static async Task InvokeTaskAsync(object target, string name, params object?[] arguments) =>
        await Assert.IsAssignableFrom<Task>(Invoke(target, name, arguments));

    private static T Field<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, name)).GetValue(target)!;

    private static void Set(object target, string name, object value) =>
        (target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, name)).SetValue(target, value);

    private sealed class StateProvider(string[] permissions) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                permissions.Select(value => new Claim("permissions", value)),
                "test"))));
    }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/Quotations/View?id=84");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class DecisionUiHandler : HttpMessageHandler
    {
        private bool detailLoaded;
        public int DecisionCalls { get; private set; }
        public int SessionCallsAfterDetail { get; private set; }
        public string? Csrf { get; private set; }
        public string? DecisionBody { get; private set; }
        public HttpStatusCode DecisionStatus { get; init; } = HttpStatusCode.OK;
        public string? DecisionResponseStatus { get; init; } = "Completed";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/bff/quotations/84")
            {
                detailLoaded = true;
                return Json(Page());
            }
            if (path == "/bff/session")
            {
                if (detailLoaded) SessionCallsAfterDetail++;
                return Json(new EmployeeSessionSummary(true, "employee", "Employee", [], "fresh-csrf", 7, [LegacyQuotationPermissions.Update]));
            }
            if (path == "/bff/quotations/84/decision")
            {
                DecisionCalls++;
                Csrf = request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null;
                DecisionBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                var response = DecisionResponseStatus is null
                    ? new HttpResponseMessage(DecisionStatus)
                    : Json(new QuotationDecisionResult(DecisionResponseStatus, 1, 2, new DateTime(2030, 7, 18, 8, 31, 0, DateTimeKind.Utc)), DecisionStatus);
                if (DecisionStatus == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(17));
                return response;
            }
            throw new InvalidOperationException($"Unexpected UI request {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = JsonContent.Create(value),
        };

        private static QuotationDetailPage Page() => new(
            new QuotationListItem(84, 42, 7, null, 30, new DateTime(2030, 8, 1), 100m, 7m, 107m, null, 107m, 1, null, null, null, null, null, new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc), new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc)),
            null,
            null,
            new QuotationCurrency(1, "THB", "Thai Baht"),
            null,
            [],
            []);
    }
}
