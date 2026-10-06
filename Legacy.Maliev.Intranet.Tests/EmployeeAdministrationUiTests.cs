using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Maliev.ShadcnBlazor.Components.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Real Razor/Shadcn rendering; only the same-origin HTTP transport is controlled.</summary>
public sealed class EmployeeAdministrationUiTests
{
    [Theory]
    [InlineData("duplicate-country")]
    [InlineData("invalid-country")]
    [InlineData("null-countries")]
    [InlineData("duplicate-role")]
    [InlineData("wrong-address")]
    [InlineData("missing-protected-setting")]
    public void Load_MalformedEditDocumentNeverEnablesMutationOrAutomaticallyReloads(string defect)
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport { InvalidEdit = defect };
        using var context = Context(handler);
        var cut = Render(context);
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, handler.EditReads);
            Assert.Empty(cut.FindAll("form"));
            Assert.Empty(cut.FindAll("button[type='submit']"));
            Assert.Contains("role=\"alert\"", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Empty(handler.Writes);
    }

    [Theory]
    [InlineData("partial", "Only some save steps completed.")]
    [InlineData("unknown", "The save outcome is unknown.")]
    [InlineData("missing-outcome", "The save outcome is unknown.")]
    [InlineData("duplicate-outcome", "The save outcome is unknown.")]
    [InlineData("non-object", "The save outcome is unknown.")]
    [InlineData("transport", "The save outcome is unknown.")]
    public async Task Save_UnconfirmedOrPartialOutcomeLocksUntilExplicitSuccessfulReload(string outcome, string message)
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport { Outcome = outcome };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);

        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(message, cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
            Assert.Equal("true", cut.Find("form").GetAttribute("data-save-locked"));
            Assert.Equal(1, handler.EditReads);
            Assert.Single(handler.Writes);
        });
        // Even a synthetic submit cannot bypass the disabled button or replay the PUT.
        await cut.Find("form").SubmitAsync();
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.EditReads);

        handler.ReadStatus = HttpStatusCode.ServiceUnavailable;
        await cut.Find("header button").ClickAsync();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("form")));
        Assert.Single(handler.Writes);

        handler.ReadStatus = HttpStatusCode.OK;
        handler.UseReloadVersions = true;
        await cut.Find("header button").ClickAsync();
        Ready(cut);
        Assert.Equal(3, handler.EditReads);
        handler.Outcome = "complete";
        await cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Equal(2, handler.Writes.Count));
        var writes = handler.Writes.ToArray();
        Assert.Equal('"' + new string('a', 64) + '"', writes[0].Body.ProfileVersion);
        Assert.Equal('"' + new string('b', 64) + '"', writes[1].Body.ProfileVersion);
        Assert.Equal('"' + new string('B', 64) + '"', writes[1].Body.IdentityVersion);
        Assert.Equal('"' + new string('d', 64) + '"', writes[1].Body.AddressVersion);
        Assert.Equal(3, handler.EditReads); // Success also never auto-readbacks.
    }

    [Fact]
    public async Task Save_ForwardsCapturedVersionsDerivedAddressAndSharedEditableFieldsOnly()
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await ChangeTextAsync(cut, "employee-edit-first", "สมชาย ใหม่");
        await ChangeTextAsync(cut, "employee-edit-email", "changed@example.invalid");
        await ChangeTextAsync(cut, "employee-edit-city", "เชียงใหม่");

        await cut.Find("form").SubmitAsync();

        var sent = Assert.Single(handler.Writes);
        Assert.Equal("/bff/employees/41/edit", sent.Path);
        Assert.Equal("controlled-ui-csrf", sent.Csrf);
        Assert.Equal("สมชาย ใหม่", sent.Body.FirstName);
        Assert.Equal("changed@example.invalid", sent.Body.Email);
        Assert.Equal("เชียงใหม่", sent.Body.Address!.City);
        Assert.Equal(77, sent.Body.CapturedHomeAddressId);
        Assert.Equal('"' + new string('a', 64) + '"', sent.Body.ProfileVersion);
        Assert.Equal('"' + new string('A', 64) + '"', sent.Body.IdentityVersion);
        Assert.Equal('"' + new string('c', 64) + '"', sent.Body.AddressVersion);
        using var body = JsonDocument.Parse(sent.Json);
        Assert.False(body.RootElement.TryGetProperty("twoFactorEnabled", out _));
        Assert.False(body.RootElement.TryGetProperty("lockoutEnd", out _));
        Assert.False(body.RootElement.TryGetProperty("identityId", out _));
        Assert.False(body.RootElement.GetProperty("address").TryGetProperty("id", out _));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("All requested save steps completed.", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        });
        await cut.Find("form").SubmitAsync();
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.EditReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_AbsentAddressOrIncompleteOptionalAddressAcceptsNoAddressStage(bool openOptionalFields)
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport { HasAddress = false };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        if (openOptionalFields)
        {
            var checkbox = cut.FindComponents<ShadcnCheckbox>().Single(component =>
                component.FindAll("#employee-edit-add-address").Count != 0);
            await cut.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));
            cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#employee-edit-line1")));
        }

        await cut.Find("form").SubmitAsync();

        var sent = Assert.Single(handler.Writes);
        Assert.Null(sent.Body.CapturedHomeAddressId);
        Assert.Null(sent.Body.AddressVersion);
        if (openOptionalFields)
        {
            Assert.NotNull(sent.Body.Address);
            Assert.Null(sent.Body.Address.AddressLine1);
            Assert.Equal(0, sent.Body.Address.CountryId);
        }
        else Assert.Null(sent.Body.Address);
        cut.WaitForAssertion(() => Assert.Contains("All requested save steps completed.", cut.Markup, StringComparison.Ordinal));
        Assert.Single(handler.Writes);
        Assert.Equal(1, handler.EditReads);
    }

    [Fact]
    public void Load_PassesAuthoritativeCountryAndRoleChoicesToRealSelectComponents()
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var countries = cut.FindComponent<ShadcnSelect<int>>().Instance.Options;
        Assert.Collection(countries,
            option => { Assert.Equal(66, option.Value); Assert.Equal("ประเทศไทย", option.Text); },
            option => { Assert.Equal(81, option.Value); Assert.Equal("Japan", option.Text); });
        var roles = cut.FindComponent<ShadcnSelect<int?>>().Instance.Options;
        Assert.Collection(roles, option => { Assert.Equal(7, option.Value); Assert.Equal("Engineer", option.Text); });
        Assert.Equal(66, cut.FindComponent<ShadcnSelect<int>>().Instance.Value);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Save_QueryChangesWhilePutIsHeldIgnoresOldReceiptAndRequiresExplicitReload()
    {
        using var culture = new EnglishCulture();
        using var handler = new EmployeeTransport { HoldPut = true };
        using var context = Context(handler);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var cut = Render(context);
        Ready(cut);
        var submission = cut.Find("form").SubmitAsync();
        try
        {
            await handler.PutEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("form").SubmitAsync();
            Assert.Single(handler.Writes);
            await cut.InvokeAsync(() => navigation.NavigateTo("/Employees/Edit?id=42"));
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("The selected employee changed during the save attempt.", cut.Markup, StringComparison.Ordinal);
                Assert.Empty(cut.FindAll("form"));
            });
            Assert.Equal(1, handler.EditReads);
        }
        finally
        {
            handler.ReleasePut.TrySetResult();
            await submission.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Single(handler.Writes);
        Assert.DoesNotContain("All requested save steps completed.", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button[type='submit']"));
        handler.HoldPut = false;
        await cut.Find("header button").ClickAsync();
        Ready(cut);
        Assert.Equal("42", cut.Find("form").GetAttribute("data-employee-id"));
        await cut.Find("form").SubmitAsync();
        Assert.Equal(new[] { "/bff/employees/41/edit", "/bff/employees/42/edit" }, handler.Writes.Select(write => write.Path));
        Assert.Equal(2, handler.EditReads);
    }

    private static BunitContext Context(EmployeeTransport handler)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Employees/Edit?id=41");
        return context;
    }

    private static IRenderedComponent<Router> Render(BunitContext context) => context.Render<Router>(parameters => parameters
        .Add(router => router.AppAssembly, typeof(EmployeeEdit).Assembly)
        .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
        {
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), route);
            builder.CloseComponent();
        })));

    private static void Ready(IRenderedComponent<Router> cut) => cut.WaitForAssertion(() =>
        Assert.False(cut.Find("button[type='submit']").HasAttribute("disabled")));

    private static Task ChangeTextAsync(IRenderedComponent<Router> cut, string id, string value)
    {
        var input = cut.FindComponents<ShadcnInput<string>>().Single(component => component.FindAll("#" + id).Count != 0);
        return cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value));
    }

    private sealed record SentWrite(string Path, EmployeeAdministrationSaveRequest Body, string Json, string? Csrf);

    private sealed class EmployeeTransport : HttpMessageHandler
    {
        public ConcurrentQueue<SentWrite> Writes { get; } = new();
        public TaskCompletionSource PutEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Outcome { get; set; } = "complete";
        public string? InvalidEdit { get; set; }
        public bool HoldPut { get; set; }
        public bool HasAddress { get; set; } = true;
        public bool UseReloadVersions { get; set; }
        public HttpStatusCode ReadStatus { get; set; } = HttpStatusCode.OK;
        private int editReads;
        public int EditReads => Volatile.Read(ref editReads);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://localhost", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/session")
                return Json(new EmployeeSessionSummary(true, "controlled-session", "Synthetic", [], "controlled-ui-csrf"));
            if (request.Method == HttpMethod.Get && path is "/bff/employees/41/edit" or "/bff/employees/42/edit")
            {
                Interlocked.Increment(ref editReads);
                if (ReadStatus != HttpStatusCode.OK) return new(ReadStatus);
                var id = path.Contains("/42/", StringComparison.Ordinal) ? 42 : 41;
                if (InvalidEdit is null) return Json(Edit(id));
                var document = JsonSerializer.SerializeToNode(Edit(id),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
                switch (InvalidEdit)
                {
                    case "duplicate-country":
                        document["countries"]!.AsArray().Add(document["countries"]![0]!.DeepClone());
                        break;
                    case "invalid-country": document["countries"]![0]!["id"] = 0; break;
                    case "null-countries": document["countries"] = null; break;
                    case "duplicate-role": document["roles"]!.AsArray().Add(document["roles"]![0]!.DeepClone()); break;
                    case "wrong-address": document["address"]!["id"] = 78; break;
                    case "missing-protected-setting": document["identity"]!.AsObject().Remove("twoFactorEnabled"); break;
                    default: throw new InvalidOperationException("Unexpected controlled edit defect.");
                }
                return Raw(document.ToJsonString());
            }
            if (request.Method != HttpMethod.Put || path is not ("/bff/employees/41/edit" or "/bff/employees/42/edit"))
                throw new InvalidOperationException("Unexpected controlled UI route.");
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Deserialize<EmployeeAdministrationSaveRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Writes.Enqueue(new(path, body, json, request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null));
            PutEntered.TrySetResult();
            if (HoldPut) await ReleasePut.Task.WaitAsync(cancellationToken);
            if (Outcome == "transport") throw new HttpRequestException("Controlled UI transport failure.");
            if (Outcome == "partial") return Json(new EmployeeAdministrationSaveResult("address", ["identity"], false, 503), HttpStatusCode.ServiceUnavailable);
            if (Outcome == "unknown") return Json(new EmployeeAdministrationSaveResult("identity", [], true, 503), HttpStatusCode.ServiceUnavailable);
            if (Outcome == "missing-outcome") return Raw("{\"stage\":\"complete\",\"completedStages\":[\"identity\",\"address\",\"profile\"],\"statusCode\":200}");
            if (Outcome == "duplicate-outcome") return Raw("{\"stage\":\"complete\",\"completedStages\":[\"identity\",\"address\",\"profile\"],\"outcomeUnknown\":true,\"OutcomeUnknown\":false,\"statusCode\":200}");
            if (Outcome == "non-object") return Raw("[]");
            var addressWritten = body.Address is { } address && (body.CapturedHomeAddressId is not null
                || (!string.IsNullOrEmpty(address.AddressLine1) && address.CountryId != 0));
            return Json(new EmployeeAdministrationSaveResult("complete", addressWritten
                ? ["identity", "address", "profile"] : ["identity", "profile"], false, 200));
        }

        private EmployeeAdministrationEdit Edit(int id) => new(
            new(id, 7, "Synthetic", "Employee", "Synthetic Employee", "+66812345678", "initial@example.invalid", null, HasAddress ? 77 : null, null, null),
            new("controlled-identity", "initial@example.invalid", "initial@example.invalid", true, "+66812345678", true, true,
                new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero), true, 0, id, new string(UseReloadVersions ? 'B' : 'A', 64)),
            HasAddress ? new(77, null, "Synthetic street", null, "Bangkok", null, null, 66, null, null) : null,
            [new(7, "Engineer", null, null, null)],
            '"' + new string(UseReloadVersions ? 'b' : 'a', 64) + '"',
            '"' + new string(UseReloadVersions ? 'B' : 'A', 64) + '"',
            HasAddress ? '"' + new string(UseReloadVersions ? 'd' : 'c', 64) + '"' : null,
            [new(66, "ประเทศไทย"), new(81, "Japan")]);

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
