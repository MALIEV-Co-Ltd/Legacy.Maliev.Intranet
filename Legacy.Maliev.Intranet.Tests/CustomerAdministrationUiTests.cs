using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Maliev.ShadcnBlazor.Components.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual CustomerEdit/Router/Shadcn rendering with only same-origin HTTP controlled; not producer or joined acceptance.</summary>
public sealed class CustomerAdministrationUiTests
{
    [Fact]
    public async Task Save_CapturesBothOriginalVersionsContactsAndFlagsWithoutProtectedSettings()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, "customer-edit-first", "สมชาย ใหม่");
        await Change(cut, "customer-edit-last", "ทดสอบ");
        await Change(cut, "customer-edit-email", "changed+customer@example.invalid");
        await Change(cut, "customer-edit-phone", "+66812345678");
        await Change(cut, "customer-edit-mobile", "+66912345678");
        await Change(cut, "customer-edit-fax", "+6621234567");
        await Toggle(cut, "customer-edit-email-confirmed", false);
        await Toggle(cut, "customer-edit-phone-confirmed", true);
        await Toggle(cut, "customer-edit-lockout", false);
        await cut.Find("form").SubmitAsync();
        var sent = Assert.Single(handler.Writes);
        Assert.Equal("/bff/customers/41/edit", sent.Path);
        Assert.Equal("controlled-ui-csrf", sent.Csrf);
        Assert.Equal("สมชาย ใหม่", sent.Body.FirstName);
        Assert.Equal("ทดสอบ", sent.Body.LastName);
        Assert.Equal("changed+customer@example.invalid", sent.Body.Email);
        Assert.Equal("+66812345678", sent.Body.Telephone);
        Assert.Equal("+66912345678", sent.Body.Mobile);
        Assert.Equal("+6621234567", sent.Body.Fax);
        Assert.Equal(new DateTime(2000, 1, 2), sent.Body.DateOfBirth);
        Assert.False(sent.Body.EmailConfirmed);
        Assert.True(sent.Body.PhoneNumberConfirmed);
        Assert.False(sent.Body.LockoutEnabled);
        Assert.Equal("\"0000000a\"", sent.Body.ProfileVersion);
        Assert.Equal('"' + new string('A', 64) + '"', sent.Body.IdentityVersion);
        using var json = JsonDocument.Parse(sent.Json);
        Assert.Equal(12, json.RootElement.EnumerateObject().Count());
        foreach (var field in new[] { "twoFactorEnabled", "lockoutEnd", "identityId", "databaseID", "companyId", "billingAddressId", "shippingAddressId" })
            Assert.False(json.RootElement.TryGetProperty(field, out _));
        Assert.Equal(1, handler.SessionReads);
    }

    [Fact]
    public async Task Save_SuccessLocksWithoutAutomaticReadbackUntilExplicitReloadCapturesNewVersions()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await cut.Find("form").SubmitAsync();
        Locked(cut);
        Assert.Contains("Customer profile and account flags were saved.", cut.Markup, StringComparison.Ordinal);
        await cut.Find("form").SubmitAsync();
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.EditReads);
        handler.UseReloadVersions = true;
        await cut.Find("header button").ClickAsync();
        Ready(cut);
        Assert.Equal(2, handler.EditReads);
        await cut.Find("form").SubmitAsync();
        var writes = handler.Writes.ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal("\"0000000a\"", writes[0].Body.ProfileVersion);
        Assert.Equal("\"0000000b\"", writes[1].Body.ProfileVersion);
        Assert.Equal('"' + new string('B', 64) + '"', writes[1].Body.IdentityVersion);
        Assert.Equal(2, handler.EditReads);
    }

    [Theory]
    [InlineData("profile-unknown")]
    [InlineData("identity-partial")]
    [InlineData("stale")]
    [InlineData("missing-outcome")]
    [InlineData("duplicate-outcome")]
    [InlineData("non-object")]
    [InlineData("wrong-order")]
    [InlineData("status-drift")]
    [InlineData("transport")]
    [InlineData("malformed")]
    public async Task Save_PartialUnknownStaleOrMalformedReceiptNeverResends(string outcome)
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport { Outcome = outcome };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await cut.Find("form").SubmitAsync();
        Locked(cut);
        Assert.Contains("role=\"alert\"", cut.Markup, StringComparison.Ordinal);
        var message = outcome switch
        {
            "identity-partial" => "The customer profile was saved, but the account update did not complete.",
            "stale" => "Customer or account data changed.",
            _ => "The save outcome could not be confirmed.",
        };
        Assert.Contains(message, cut.Markup, StringComparison.Ordinal);
        await cut.Find("form").SubmitAsync();
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.SessionReads);
        Assert.Equal(1, handler.EditReads);
    }

    [Theory]
    [InlineData("profile-id")]
    [InlineData("identity-id")]
    [InlineData("profile-version")]
    [InlineData("identity-version")]
    [InlineData("missing-protected")]
    public void Load_InvalidBoundProjectionNeverEnablesSave(string defect)
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport { InvalidEdit = defect };
        using var context = Context(handler);
        var cut = Render(context);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("role=\"alert\"", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("form"));
        });
        Assert.Equal(1, handler.EditReads);
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Writes);
    }

    [Theory]
    [InlineData("apostrophe")]
    [InlineData("non-ascii")]
    [InlineData("too-long")]
    public async Task Save_EmailOutsideProducerPolicyRejectsBeforeSessionOrPut(string scenario)
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var email = scenario switch
        {
            "apostrophe" => "o'connor@example.invalid",
            "non-ascii" => "สมชาย@example.invalid",
            _ => new string('a', 241) + "@example.invalid", // Exactly 257 characters.
        };
        Assert.True(new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email));
        if (scenario == "too-long") Assert.Equal(257, email.Length);
        Assert.Equal("256", cut.Find("#customer-edit-email").GetAttribute("maxlength"));
        await Change(cut, "customer-edit-email", email);
        await cut.Find("form").SubmitAsync();
        Assert.Contains("Check the highlighted fields before saving.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Save_QueryChangeCancelsHeldPutAndIgnoresLateSuccessUntilExplicitReload()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport { HoldPut = true };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var pending = cut.Find("form").SubmitAsync();
        await WithHeldCleanup(pending, handler.ReleasePut, async () =>
        {
            await handler.PutEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.InvokeAsync(() => context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Customers/Edit?id=42"));
            cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("form")));
            Assert.True(handler.PutToken.IsCancellationRequested);
            Assert.Equal(1, handler.EditReads);
        });
        Assert.DoesNotContain("Customer profile and account flags were saved.", cut.Markup, StringComparison.Ordinal);
        handler.HoldPut = false;
        await cut.Find("header button").ClickAsync();
        Ready(cut);
        Assert.Equal("42", cut.Find("form").GetAttribute("data-customer-id"));
        Assert.Single(handler.Writes);
    }

    [Fact]
    public async Task Save_DisposeCancelsHeldPutWithoutLateReadbackOrReplay()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport { HoldPut = true };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var pending = cut.Find("form").SubmitAsync();
        await WithHeldCleanup(pending, handler.ReleasePut, async () =>
        {
            await handler.PutEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.Dispose();
            Assert.True(handler.PutToken.IsCancellationRequested);
        });
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.EditReads);
    }

    [Fact]
    public async Task Load_QueryChangeCancelsHeldReadAndLateOldDocumentCannotReplaceNewCustomer()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        handler.HoldRead = true;
        var pending = cut.Find("header button").ClickAsync();
        await WithHeldCleanup(pending, handler.ReleaseRead, async () =>
        {
            await handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.InvokeAsync(() => context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Customers/Edit?id=42"));
            Ready(cut);
            Assert.Equal("42", cut.Find("form").GetAttribute("data-customer-id"));
            Assert.True(handler.ReadToken.IsCancellationRequested);
        });
        Assert.Equal("42", cut.Find("form").GetAttribute("data-customer-id"));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Load_DisposeCancelsHeldReadWithoutSessionOrWrite()
    {
        using var culture = new EnglishCulture();
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        handler.HoldRead = true;
        var pending = cut.Find("header button").ClickAsync();
        await WithHeldCleanup(pending, handler.ReleaseRead, async () =>
        {
            await handler.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.Dispose();
            Assert.True(handler.ReadToken.IsCancellationRequested);
        });
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Writes);
    }

    private static async Task WithHeldCleanup(Task pending, TaskCompletionSource release, Func<Task> assertions)
    {
        var primaryFailed = false;
        try { await assertions(); }
        catch { primaryFailed = true; throw; }
        finally
        {
            release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch when (primaryFailed) { /* Preserve the primary assertion failure after attempting the bounded drain. */ }
        }
    }

    private static BunitContext Context(Transport handler)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Customers/Edit?id=41");
        return context;
    }
    private static IRenderedComponent<Router> Render(BunitContext context) => context.Render<Router>(parameters => parameters
        .Add(router => router.AppAssembly, typeof(CustomerEdit).Assembly)
        .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
        {
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), route);
            builder.CloseComponent();
        })));
    private static void Ready(IRenderedComponent<Router> cut) => cut.WaitForAssertion(() => Assert.False(cut.Find("button[type='submit']").HasAttribute("disabled")));
    private static void Locked(IRenderedComponent<Router> cut) => cut.WaitForAssertion(() =>
    {
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        Assert.Equal("true", cut.Find("form").GetAttribute("data-save-locked"));
    });
    private static Task Change(IRenderedComponent<Router> cut, string id, string value) => cut.InvokeAsync(() =>
        cut.FindComponents<ShadcnInput<string>>().Single(component => component.FindAll("#" + id).Count != 0).Instance.ValueChanged.InvokeAsync(value));
    private static Task Toggle(IRenderedComponent<Router> cut, string id, bool value) => cut.InvokeAsync(() =>
        cut.FindComponents<ShadcnCheckbox>().Single(component => component.FindAll("#" + id).Count != 0).Instance.ValueChanged.InvokeAsync(value));
    private sealed record SentWrite(string Path, CustomerAdministrationSaveRequest Body, string Json, string? Csrf);

    private sealed class Transport : HttpMessageHandler
    {
        public ConcurrentQueue<SentWrite> Writes { get; } = new();
        public TaskCompletionSource PutEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken PutToken { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public string Outcome { get; set; } = "complete";
        public string? InvalidEdit { get; init; }
        public bool HoldPut { get; set; }
        public bool HoldRead { get; set; }
        public bool UseReloadVersions { get; set; }
        private int editReads;
        private int sessionReads;
        public int EditReads => Volatile.Read(ref editReads);
        public int SessionReads => Volatile.Read(ref sessionReads);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://localhost", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/session")
            {
                Interlocked.Increment(ref sessionReads);
                return Json(new EmployeeSessionSummary(true, "controlled-session", "Synthetic", [], "controlled-ui-csrf"));
            }
            if (request.Method == HttpMethod.Get && path is "/bff/customers/41/edit" or "/bff/customers/42/edit")
            {
                Interlocked.Increment(ref editReads);
                var id = path.Contains("/42/", StringComparison.Ordinal) ? 42 : 41;
                if (HoldRead && id == 41)
                {
                    ReadToken = cancellationToken;
                    ReadEntered.TrySetResult();
                    await ReleaseRead.Task.WaitAsync(TimeSpan.FromSeconds(10)); // Deliberately return late despite cancellation.
                }
                var document = JsonSerializer.SerializeToNode(Edit(id), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
                switch (InvalidEdit)
                {
                    case "profile-id": document["profile"]!["id"] = 999; break;
                    case "identity-id": document["identity"]!["databaseID"] = 999; break;
                    case "profile-version": document["profileVersion"] = "W/\"0000000a\""; break;
                    case "identity-version": document["identityVersion"] = '"' + new string('B', 64) + '"'; break;
                    case "missing-protected": document["identity"]!.AsObject().Remove("twoFactorEnabled"); break;
                }
                return Raw(document.ToJsonString());
            }
            if (request.Method != HttpMethod.Put || path is not ("/bff/customers/41/edit" or "/bff/customers/42/edit"))
                throw new InvalidOperationException("Unexpected controlled customer administration route.");
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Deserialize<CustomerAdministrationSaveRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Writes.Enqueue(new(path, body, json, request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null));
            PutToken = cancellationToken;
            PutEntered.TrySetResult();
            if (HoldPut) await ReleasePut.Task.WaitAsync(TimeSpan.FromSeconds(10)); // Late acknowledgement tests the component fence.
            return Outcome switch
            {
                "profile-unknown" => Json(new CustomerAdministrationSaveResult("profile", [], true, 503), HttpStatusCode.ServiceUnavailable),
                "identity-partial" => Json(new CustomerAdministrationSaveResult("identity", ["profile"], false, 412), HttpStatusCode.PreconditionFailed),
                "stale" => Json(new CustomerAdministrationSaveResult("preflight", [], false, 412), HttpStatusCode.PreconditionFailed),
                "missing-outcome" => Raw("{\"stage\":\"complete\",\"completedStages\":[\"profile\",\"identity\"],\"statusCode\":200}"),
                "duplicate-outcome" => Raw("{\"stage\":\"complete\",\"completedStages\":[\"profile\",\"identity\"],\"outcomeUnknown\":true,\"OutcomeUnknown\":false,\"statusCode\":200}"),
                "non-object" => Raw("[]"),
                "wrong-order" => Json(new CustomerAdministrationSaveResult("complete", ["identity", "profile"], false, 200)),
                "status-drift" => Json(new CustomerAdministrationSaveResult("complete", ["profile", "identity"], false, 200), HttpStatusCode.ServiceUnavailable),
                "transport" => throw new HttpRequestException("Controlled UI transport failure."),
                "malformed" => Raw("{"),
                _ => Json(new CustomerAdministrationSaveResult("complete", ["profile", "identity"], false, 200)),
            };
        }
        private CustomerAdministrationEdit Edit(int id) => new(
            new(id, "Synthetic", "Customer", "Synthetic Customer", "+6621234567", null, null,
                "initial@example.invalid", new DateTime(2000, 1, 2), null, null, null, null, null, null, null, null),
            new("controlled-identity", "initial@example.invalid", "initial@example.invalid", true, "+6621234567", false,
                true, new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero), true, 0, id, null, null,
                new string(UseReloadVersions ? 'B' : 'A', 64)),
            UseReloadVersions ? "\"0000000b\"" : "\"0000000a\"",
            '"' + new string(UseReloadVersions ? 'B' : 'A', 64) + '"');
        protected override void Dispose(bool disposing)
        {
            ReleasePut.TrySetResult();
            ReleaseRead.TrySetResult();
            base.Dispose(disposing);
        }
        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Raw(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private sealed class EnglishCulture : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        public EnglishCulture() => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
}
