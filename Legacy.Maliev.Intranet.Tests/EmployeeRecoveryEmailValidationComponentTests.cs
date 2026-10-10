using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeRecoveryEmailValidationComponentTests
{
    [Theory]
    [InlineData(false, "en", "format")]
    [InlineData(false, "en", "length")]
    [InlineData(false, "en", "quoted")]
    [InlineData(false, "en", "display")]
    [InlineData(false, "en", "whitespace")]
    [InlineData(false, "th", "format")]
    [InlineData(false, "th", "length")]
    [InlineData(false, "th", "quoted")]
    [InlineData(false, "th", "display")]
    [InlineData(false, "th", "whitespace")]
    [InlineData(true, "en", "format")]
    [InlineData(true, "en", "length")]
    [InlineData(true, "en", "quoted")]
    [InlineData(true, "en", "display")]
    [InlineData(true, "en", "whitespace")]
    [InlineData(true, "th", "format")]
    [InlineData(true, "th", "length")]
    [InlineData(true, "th", "quoted")]
    [InlineData(true, "th", "display")]
    [InlineData(true, "th", "whitespace")]
    [InlineData(false, "en", "maximum")]
    [InlineData(false, "th", "maximum")]
    [InlineData(true, "en", "maximum")]
    [InlineData(true, "th", "maximum")]
    public async Task HistoricalSyntaxGate_BlocksInvalidHttpAndPreservesAcceptedLiteral(bool resend, string language, string syntax)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var handler = new Transport(resend);
            using var context = new BunitContext();
            context.Services.AddLocalization();
            context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://localhost/") });
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.GetRequiredService<NavigationManager>().NavigateTo(resend
                ? "/Employees/ResendConfirmation" : "/Employees/ForgotPassword");
            var cut = context.Render<Router>(parameters => parameters
                .Add(router => router.AppAssembly, typeof(EmployeeForgotPassword).Assembly)
                .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
                {
                    builder.OpenComponent<RouteView>(0);
                    builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                    builder.CloseComponent();
                })));
            Assert.True(cut.Find("form").HasAttribute("novalidate"));
            Assert.Single(cut.FindAll("input[type=email]"));
            var input = Assert.Single(cut.FindComponents<ShadcnInput<string>>());
            var invalid = syntax switch
            {
                "length" => new string('a', 309) + "@maliev.test",
                "maximum" => new string('a', 308) + "@maliev.test",
                "quoted" => "\"recovery@office\"@maliev.test",
                "display" => "Recovery <recovery@maliev.test>",
                "whitespace" => " recovery@maliev.test ",
                _ => "invalid-address",
            };
            await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(invalid));
            await cut.Find("form").SubmitAsync();
            if (syntax == "length") Assert.Equal(321, invalid.Length);
            if (syntax == "maximum") Assert.Equal(320, invalid.Length);
            if (syntax is "quoted" or "maximum")
            {
                Assert.Equal(invalid, new System.Net.Mail.MailAddress(invalid).Address);
                if (syntax == "quoted")
                    Assert.False(new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(invalid));
                cut.WaitForAssertion(() => Assert.Equal(invalid, Assert.Single(handler.Emails)));
                Assert.Equal(1, handler.SessionReads);
                return;
            }
            Assert.Equal(0, handler.SessionReads);
            Assert.Empty(handler.Emails);
            Assert.Contains(language == "th" ? "โปรดระบุอีเมลที่ถูกต้อง" : "Enter a valid email address", cut.Find("main").TextContent);
            await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("recovery@maliev.test"));
            await cut.Find("form").SubmitAsync();
            cut.WaitForAssertion(() => Assert.Equal("recovery@maliev.test", Assert.Single(handler.Emails)));
            Assert.Equal(1, handler.SessionReads);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private sealed class Transport(bool resend) : HttpMessageHandler
    {
        public int SessionReads { get; private set; }
        public List<string> Emails { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/bff/session", request.RequestUri!.AbsolutePath);
                SessionReads++;
                return new(HttpStatusCode.OK)
                { Content = JsonContent.Create(new EmployeeSessionSummary(false, null, null, [], "csrf-test")) };
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(resend ? "/bff/employee-recovery/email-confirmation/request" : "/bff/employee-recovery/password-reset/request", request.RequestUri!.AbsolutePath);
            Assert.Equal("csrf-test", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
            Emails.Add((await request.Content!.ReadFromJsonAsync<EmployeeRecoveryEmailRequest>(cancellationToken: cancellationToken))!.Email);
            return new(HttpStatusCode.Accepted);
        }
    }
}
