using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

// Real Razor; controlled same-origin HTTP, not producer proof.
public sealed class EmployeeAdministrationReadbackRegressionTests
{
    [Theory]
    [InlineData("en-US", "address")]
    [InlineData("en-US", "profile")]
    [InlineData("th-TH", "address")]
    [InlineData("th-TH", "profile")]
    public async Task PartialIdentityCommit_ExplicitReloadDisplaysBothStoredValuesWithoutSynchronizing(string language, string failedStage)
    {
        using var culture = new CultureScope(language);
        using var transport = new PartialCommitTransport(failedStage);
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Employees/Edit?id=41");
        var cut = context.Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(EmployeeEdit).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            })));
        Ready(cut);
        var email = cut.FindComponents<ShadcnInput<string>>().Single(component => component.FindAll("#employee-edit-email").Count != 0);
        await cut.InvokeAsync(() => email.Instance.ValueChanged.InvokeAsync(PartialCommitTransport.NewEmail));
        await cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled")));
        Assert.Equal(1, transport.EditReads);
        Assert.Single(transport.Writes);
        await cut.Find("form").SubmitAsync();
        Assert.Single(transport.Writes);

        await cut.Find("header button").ClickAsync();
        Ready(cut);
        Assert.Equal(2, transport.EditReads);
        Assert.Single(transport.Writes); // Readback cannot repair, replay, or synchronize.

        // Explicit readback must expose the stored divergence without changing the editable model.
        Assert.Equal(PartialCommitTransport.NewEmail, cut.Find("#employee-edit-stored-identity-email").TextContent.Trim());
        Assert.Equal(PartialCommitTransport.NewEmail, cut.Find("#employee-edit-stored-identity-username").TextContent.Trim());
        Assert.Equal(PartialCommitTransport.OldEmail, cut.Find("#employee-edit-stored-profile-email").TextContent.Trim());
        var warning = cut.Find("#employee-edit-identity-profile-divergence");
        Assert.Equal("status", warning.GetAttribute("role"));
        Assert.Equal(language == "th-TH"
            ? "อีเมลบัญชีและอีเมลโปรไฟล์ที่บันทึกไว้ไม่ตรงกัน ตรวจสอบทั้งสองค่าก่อนบันทึกอีกครั้ง"
            : "Stored account and profile emails differ. Review both values before saving again.", warning.TextContent.Trim());
        Assert.Empty(warning.QuerySelectorAll("input, select, textarea, button"));
        email = cut.FindComponents<ShadcnInput<string>>().Single(component => component.FindAll("#employee-edit-email").Count != 0);
        Assert.Equal(PartialCommitTransport.OldEmail, email.Instance.Value); // No automatic model synchronization.
        Assert.Empty(cut.FindAll("input[name='twoFactorEnabled'], input[name='lockoutEnd']"));
        Assert.Contains(language == "th-TH" ? "เปิดใช้งาน" : "Enabled", cut.Find(".employee-edit__protected").TextContent, StringComparison.Ordinal);

        // A distinct manual submission must use the explicit readback's fresh versions, never the original ones.
        await cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Equal(2, transport.Writes.Count));
        var second = transport.Writes[1];
        Assert.Equal(PartialCommitTransport.OldEmail, second.Request.Email);
        Assert.Equal(Version('b'), second.Request.ProfileVersion);
        Assert.Equal(Version('B'), second.Request.IdentityVersion);
        Assert.Equal(Version('d'), second.Request.AddressVersion);
        Assert.Equal(77, second.Request.CapturedHomeAddressId);
        using var json = JsonDocument.Parse(second.Json);
        Assert.False(json.RootElement.TryGetProperty("twoFactorEnabled", out _));
        Assert.False(json.RootElement.TryGetProperty("lockoutEnd", out _));
        Assert.False(json.RootElement.TryGetProperty("identityId", out _));
        Assert.Equal(2, transport.EditReads);
    }

    private static string Version(char value) => '"' + new string(value, 64) + '"';
    private static void Ready(IRenderedComponent<Router> cut) => cut.WaitForAssertion(() =>
        Assert.False(cut.Find("button[type='submit']").HasAttribute("disabled")));
    private sealed record Write(EmployeeAdministrationSaveRequest Request, string Json);

    private sealed class PartialCommitTransport(string failedStage) : HttpMessageHandler
    {
        internal const string OldEmail = "old-profile@example.invalid";
        internal const string NewEmail = "committed-account@example.invalid";
        public List<Write> Writes { get; } = [];
        public int EditReads { get; private set; }
        private bool identityCommitted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://localhost", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Null(request.Headers.Authorization);
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/session")
                return Json(new EmployeeSessionSummary(true, "synthetic", "Synthetic", [], "controlled-readback-csrf"));
            if (request.Method == HttpMethod.Get && path == "/bff/employees/41/edit")
            {
                EditReads++;
                return Json(Edit());
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/bff/employees/41/edit", path);
            Assert.Equal("controlled-readback-csrf", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Deserialize<EmployeeAdministrationSaveRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Writes.Add(new(body, json));
            if (Writes.Count == 1)
            {
                Assert.Equal(NewEmail, body.Email);
                identityCommitted = true; // Deliberate controlled partial outcome, not an actual producer effect.
                return Json(new EmployeeAdministrationSaveResult(failedStage,
                    failedStage == "address" ? ["identity"] : ["identity", "address"], false, 412), HttpStatusCode.PreconditionFailed);
            }
            return Json(new EmployeeAdministrationSaveResult("complete", ["identity", "address", "profile"], false, 200));
        }
        private EmployeeAdministrationEdit Edit() => new(
            new(41, 7, "Synthetic", "Employee", "Synthetic Employee", "+66812345678", OldEmail, null, 77, null, null),
            new("controlled-identity", identityCommitted ? NewEmail : OldEmail, identityCommitted ? NewEmail : OldEmail,
                true, "+66812345678", true, true, new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero), true, 0, 41,
                new string(identityCommitted ? 'B' : 'A', 64)),
            new(77, null, "Synthetic street", null, "Bangkok", null, null, 66, null, null),
            [new(7, "Engineer", null, null, null)], Version(identityCommitted ? 'b' : 'a'),
            Version(identityCommitted ? 'B' : 'A'), Version(identityCommitted ? 'd' : 'c'), [new(66, "ประเทศไทย")]);
        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(value) };
    }
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
