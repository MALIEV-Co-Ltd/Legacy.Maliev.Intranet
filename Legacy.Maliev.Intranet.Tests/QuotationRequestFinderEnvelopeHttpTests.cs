extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using BffProgram = Bff::Program;
using QuotationRequestFilesProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestFilesProxy;
using QuotationRequestsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestsProxy;
using RequestView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.View;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Compiled editor load/save through the real cookie/CSRF BFF; only external services are controlled.</summary>
public sealed class QuotationRequestFinderEnvelopeHttpTests
{
    internal const string Envelope = """
        {"source":"service_finder","version":1,"answers":{"files":"files-real-part","service":"service-3d","material":"material-plastic","quantity":"quantity-1-10","end-use":"use-prototype","performance":"performance-strength","environment":"environment-outdoor"},"recommended_service_ids":["scanning","design"],"finder_path":["scanning","design"],"operator_comment":"original staff note","future_context":{"keep":"unchanged"}}
        """;

    [Fact]
    public async Task Load_FinderEnvelope_ExposesOnlyOperatorNoteForEditing()
    {
        using var downstream = new QuotationBoundary(Envelope);
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);

        Assert.Equal("original staff note", EditNote(view));
    }

    [Theory]
    [InlineData("files-real-part", true)]
    [InlineData("files-3d", false)]
    public async Task Save_FinderEnvelope_PreservesRequiredAndOptionalContextAtQuotationBoundary(string files, bool optional)
    {
        var original = Envelope.Replace("files-real-part", files, StringComparison.Ordinal);
        if (!optional)
        {
            original = original.Replace(",\"performance\":\"performance-strength\",\"environment\":\"environment-outdoor\"", string.Empty, StringComparison.Ordinal);
        }
        using var downstream = new QuotationBoundary(original);
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);

        SetEditNote(view, "reviewed by staff");
        await InvokeTaskAsync(view, "SaveAsync");

        Assert.Equal("Saved", Field<string>(view, "notice"));
        Assert.Null(Field<string?>(view, "error"));
        Assert.Equal("2030-07-18T08:30:00.0000000Z", downstream.ExpectedModifiedDate);
        Assert.Equal("Bearer fixture-service-token", downstream.Authorization);
        AssertEnvelope(downstream.InternalComment, files, optional, "reviewed by staff");
        var loaded = Assert.IsType<QuotationRequestDetail>(Field<object>(view, "detail"));
        Assert.Equal(downstream.InternalComment, loaded.Request.InternalComment);
        Assert.Equal("reviewed by staff", EditNote(view));
    }

    [Fact]
    public async Task Save_ClearedOperatorNote_RetainsFinderEnvelopeAndExtensionProperties()
    {
        using var downstream = new QuotationBoundary(Envelope);
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);

        SetEditNote(view, string.Empty);
        await InvokeTaskAsync(view, "SaveAsync");

        AssertEnvelope(downstream.InternalComment, "files-real-part", true, null);
    }

    [Fact]
    public async Task Save_OperatorNote_TrimsOnlyTheNoteAndPreservesUnknownAnswerExtension()
    {
        var original = Envelope.Replace("\"files\":", "\"future_answer\":{\"nested\":[1,true,null]},\"files\":", StringComparison.Ordinal);
        using var downstream = new QuotationBoundary(original);
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);

        SetEditNote(view, "  reviewed by staff  ");
        await InvokeTaskAsync(view, "SaveAsync");

        AssertEnvelope(downstream.InternalComment, "files-real-part", true, "reviewed by staff");
        using var result = JsonDocument.Parse(downstream.InternalComment!);
        Assert.Equal("{\"nested\":[1,true,null]}", result.RootElement.GetProperty("answers").GetProperty("future_answer").GetRawText());
    }

    [Theory]
    [MemberData(nameof(InvalidEnvelopes))]
    public async Task Save_InvalidOrAmbiguousEnvelope_UsesOrdinaryNoteFallbackWithoutCrash(string original)
    {
        using var downstream = new QuotationBoundary(original);
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);
        Assert.Equal(original, EditNote(view));

        SetEditNote(view, "plain reviewed note");
        await InvokeTaskAsync(view, "SaveAsync");

        Assert.Equal("plain reviewed note", downstream.InternalComment);
        Assert.Equal("Saved", Field<string>(view, "notice"));
        Assert.Null(Field<string?>(view, "error"));
    }

    public static IEnumerable<object[]> InvalidEnvelopes()
    {
        yield return ["ordinary staff note"];
        yield return ["{"];
        yield return ["null"];
        yield return ["[]"];
        yield return [Envelope.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"version\":1", "\"version\":\"1\"", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"source\":\"service_finder\"", "\"source\":\"future_finder\"", StringComparison.Ordinal)];
        yield return [Envelope.Replace("files-real-part", "untrusted-file-id", StringComparison.Ordinal)];
        yield return [Envelope.Replace("performance-strength", "untrusted-performance-id", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"finder_path\":[\"scanning\",\"design\"]", "\"finder_path\":[\"printing\"]", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"recommended_service_ids\":[\"scanning\",\"design\"]", "\"recommended_service_ids\":[\"scanning\",\"scanning\"]", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"source\":", "\"source\":\"service_finder\",\"source\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"version\":", "\"version\":1,\"version\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"operator_comment\":", "\"operator_comment\":\"duplicate\",\"operator_comment\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"files\":", "\"files\":\"files-real-part\",\"files\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"performance\":", "\"performance\":\"performance-strength\",\"performance\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"future_context\":", "\"future_context\":0,\"future_context\":", StringComparison.Ordinal)];
        foreach (var malformedNote in new[] { "42", "{\"nested\":true}", "[]", "null" })
            yield return [Envelope.Replace("\"operator_comment\":\"original staff note\"", $"\"operator_comment\":{malformedNote}", StringComparison.Ordinal)];
    }

    [Fact]
    public async Task Save_PlainOperatorNote_KeepsOrdinaryStringAndConcurrencyHeader()
    {
        using var downstream = new QuotationBoundary("ordinary original note");
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);
        Assert.Equal("ordinary original note", EditNote(view));

        SetEditNote(view, "ordinary updated note");
        await InvokeTaskAsync(view, "SaveAsync");

        Assert.Equal("ordinary updated note", downstream.InternalComment);
        Assert.Equal("2030-07-18T08:30:00.0000000Z", downstream.ExpectedModifiedDate);
        Assert.Equal("ordinary updated note", EditNote(view));
        Assert.Equal("Saved", Field<string>(view, "notice"));
        Assert.Equal(1, downstream.UpdateCalls);
    }

    [Fact]
    public async Task Save_StaleTimestamp_ReturnsConflictWithoutReloadOrSuccessfulMutation()
    {
        using var downstream = new QuotationBoundary(Envelope) { UpdateStatus = HttpStatusCode.Conflict };
        await using var factory = new Factory(downstream);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);
        var originalDetailCalls = downstream.DetailCalls;

        SetEditNote(view, "stale staff note");
        await InvokeTaskAsync(view, "SaveAsync");

        Assert.Equal("Conflict", Field<string>(view, "error"));
        Assert.Null(Field<string?>(view, "notice"));
        Assert.Equal(Envelope, downstream.InternalComment);
        Assert.Equal(originalDetailCalls, downstream.DetailCalls);
        Assert.Equal("2030-07-18T08:30:00.0000000Z", downstream.ExpectedModifiedDate);
        Assert.False(Field<bool>(view, "submitting"));
    }

    [Fact]
    public async Task Save_EmployeeWithoutUpdatePermission_NeverReachesQuotationWrite()
    {
        using var downstream = new QuotationBoundary(Envelope);
        await using var factory = new Factory(downstream, canUpdate: false);
        using var client = await SignInAsync(factory);
        var view = await LoadViewAsync(client);

        SetEditNote(view, "unauthorized staff note");
        await InvokeTaskAsync(view, "SaveAsync");

        Assert.Equal("Forbidden", Field<string>(view, "error"));
        Assert.Equal(0, downstream.UpdateCalls);
        Assert.Equal(Envelope, downstream.InternalComment);
    }

    private static void AssertEnvelope(string? raw, string files, bool optional, string? note)
    {
        Assert.True(raw?.StartsWith('{') == true, $"Reserved service-finder envelope was replaced by operator text: {raw}");
        using var document = JsonDocument.Parse(raw!);
        var root = document.RootElement;
        Assert.Equal("service_finder", root.GetProperty("source").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var answers = root.GetProperty("answers");
        Assert.Equal(files, answers.GetProperty("files").GetString());
        Assert.Equal("service-3d", answers.GetProperty("service").GetString());
        Assert.Equal("material-plastic", answers.GetProperty("material").GetString());
        Assert.Equal("quantity-1-10", answers.GetProperty("quantity").GetString());
        Assert.Equal("use-prototype", answers.GetProperty("end-use").GetString());
        Assert.Equal(optional, answers.TryGetProperty("performance", out var performance));
        Assert.Equal(optional, answers.TryGetProperty("environment", out var environment));
        if (optional)
        {
            Assert.Equal("performance-strength", performance.GetString());
            Assert.Equal("environment-outdoor", environment.GetString());
        }
        Assert.Equal(["scanning", "design"], root.GetProperty("recommended_service_ids").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["scanning", "design"], root.GetProperty("finder_path").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("unchanged", root.GetProperty("future_context").GetProperty("keep").GetString());
        if (note is null) Assert.False(root.TryGetProperty("operator_comment", out _));
        else Assert.Equal(note, root.GetProperty("operator_comment").GetString());
    }

    private static async Task<HttpClient> SignInAsync(Factory factory)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        using var anonymous = await client.GetAsync("/bff/session");
        var session = await anonymous.Content.ReadFromJsonAsync<JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "finder-fixture@maliev.com", password = "fixture-only", returnUrl = "/QuotationRequests/View?id=9" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(login);
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<RequestView> LoadViewAsync(HttpClient client)
    {
        var view = new RequestView { Id = 9 };
        SetProperty(view, "Http", client);
        SetProperty(view, "Navigation", new FixtureNavigationManager());
        SetProperty(view, "Text", new KeyLocalizer<RequestView>());
        await InvokeTaskAsync(view, "LoadAsync");
        Assert.Null(Field<string?>(view, "error"));
        Assert.Equal(9, Assert.IsType<QuotationRequestDetail>(Field<object>(view, "detail")).Request.Id);
        return view;
    }

    // Invoke compiled Razor behavior, not a copied editor model or source-code string assertion.
    private static async Task InvokeTaskAsync(object target, string method) => await Assert.IsAssignableFrom<Task>(
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
    private static string? EditNote(RequestView view) => (string?)Field<object>(view, "edit").GetType().GetProperty("InternalComment")!.GetValue(Field<object>(view, "edit"));
    private static void SetEditNote(RequestView view, string note) => SetProperty(Field<object>(view, "edit"), "InternalComment", note);

    private sealed class Factory(QuotationBoundary downstream, bool canUpdate = true) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new FixtureAuthClient(canUpdate));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokens = new FixtureServiceTokens();
                services.AddSingleton<IServiceAccessTokenProvider>(tokens);
                var auth = new LegacyServiceAuthenticationHandler(tokens) { InnerHandler = downstream };
                var client = new HttpClient(auth, disposeHandler: false) { BaseAddress = new("https://quotation.example.test/") };
                services.RemoveAll<QuotationRequestsProxy>();
                services.AddSingleton(new QuotationRequestsProxy(client));
                services.RemoveAll<QuotationRequestFilesProxy>();
                services.AddSingleton(new QuotationRequestFilesProxy(client));
            });
        }
    }

    private sealed class QuotationBoundary(string initialComment) : HttpMessageHandler
    {
        public string? InternalComment { get; private set; } = initialComment;
        public string? ExpectedModifiedDate { get; private set; }
        public string? Authorization { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public HttpStatusCode UpdateStatus { get; init; } = HttpStatusCode.NoContent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/quotationrequests/9")
            {
                DetailCalls++;
                return Json(new QuotationRequestItem(9, "Buyer", "Fixture", "buyer@example.test", "0690", "TH", "Fixture", null,
                    "part review", InternalComment, false, new DateTime(2030, 7, 18, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc)));
            }
            if (request.Method == HttpMethod.Get && path == "/quotationrequests/9/files") return Json(Array.Empty<QuotationRequestFileItem>());
            if (request.Method == HttpMethod.Get && path == "/quotationrequests/9/qualification-receipt") return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Put && path == "/quotationrequests/9")
            {
                UpdateCalls++;
                ExpectedModifiedDate = request.Headers.GetValues("X-Expected-Modified-Date").Single();
                Authorization = request.Headers.Authorization?.ToString();
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("Buyer", body.RootElement.GetProperty("firstName").GetString());
                Assert.Equal("part review", body.RootElement.GetProperty("message").GetString());
                Assert.False(body.RootElement.TryGetProperty("modifiedDate", out _));
                if (UpdateStatus == HttpStatusCode.NoContent) InternalComment = body.RootElement.GetProperty("internalComment").GetString();
                return new(UpdateStatus);
            }
            throw new InvalidOperationException($"Unexpected external boundary {request.Method} {path}");
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private sealed class FixtureServiceTokens : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("fixture-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class FixtureAuthClient(bool canUpdate) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            string[] read = [LegacyEmployeePermissions.QuotationRequestsRead, LegacyEmployeePermissions.QuotationFilesRead, LegacyEmployeePermissions.FileUploadsRead];
            var permissions = canUpdate ? read.Append(LegacyEmployeePermissions.QuotationRequestsUpdate).ToArray() : read;
            return Task.FromResult(new EmployeeLoginResult(true, new("fixture-access", "fixture-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddHours(1)), new("employee", "Fixture", email, permissions, 7)));
        }
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class FixtureNavigationManager : NavigationManager
    {
        public FixtureNavigationManager() => Initialize("https://localhost/", "https://localhost/QuotationRequests/View?id=9");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
