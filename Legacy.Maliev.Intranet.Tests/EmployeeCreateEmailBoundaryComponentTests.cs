using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
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

/// <summary>Exercises real creation validation and Shadcn rendering; only same-origin HTTP is controlled.</summary>
public sealed class EmployeeCreateEmailBoundaryComponentTests
{
    [Theory]
    [InlineData(255, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void CreationModel_EmailBoundaryMatchesProfileStorage(int length, bool expectedValid)
    {
        var model = Model(Email(length));
        var propertyResults = new List<ValidationResult>();
        var propertyContext = new ValidationContext(model) { MemberName = nameof(model.Email) };

        Assert.Equal(expectedValid, Validator.TryValidateProperty(model.Email, propertyContext, propertyResults));

        var modelResults = new List<ValidationResult>();
        Assert.Equal(expectedValid, Validator.TryValidateObject(model, new ValidationContext(model), modelResults, true));
        if (expectedValid)
        {
            Assert.Empty(propertyResults);
            Assert.Empty(modelResults);
        }
        else
        {
            Assert.Single(propertyResults);
            var failure = Assert.Single(modelResults);
            Assert.Contains(nameof(model.Email), failure.MemberNames);
        }
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    public async Task ValidBoundary_SubmitsExactEmailOnceWithSessionCsrfAndCompleteRequiredFields(int length)
    {
        using var culture = new CultureScope("en");
        using var handler = new CreationTransport();
        using var context = Context(handler);
        var cut = Render(context);
        var email = Email(length);
        await FillAsync(cut, email);

        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("a[href='/Employees/View?id=42']")));
        Assert.Equal(1, handler.SessionReads);
        var sent = Assert.Single(handler.Creates);
        Assert.Equal("controlled-create-csrf", sent.Csrf);
        Assert.Null(sent.Authorization);
        using var body = JsonDocument.Parse(sent.Json);
        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Synthetic", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("Employee", body.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("synthetic-password", body.RootElement.GetProperty("password").GetString());
        Assert.Equal("synthetic-password", body.RootElement.GetProperty("confirmPassword").GetString());
        Assert.Equal("+66812345678", body.RootElement.GetProperty("phoneNumber").GetString());
        Assert.False(body.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(body.RootElement.TryGetProperty("homeAddressId", out _));
    }

    [Theory]
    [InlineData("en", "Email must be 256 characters or fewer.")]
    [InlineData("th", "อีเมลต้องมีความยาวไม่เกิน 256 ตัวอักษร")]
    public async Task OverlongEmail_IsRejectedLocallyWithLocalizedFieldErrorAndNoHttpRequests(
        string language, string expectedError)
    {
        using var culture = new CultureScope(language);
        using var handler = new CreationTransport();
        using var context = Context(handler);
        var cut = Render(context);
        // Invoke the real binding callback to also cover programmatic/pasted values
        // that can exceed the rendered browser maxlength constraint.
        await FillAsync(cut, Email(257));

        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(expectedError, cut.Find("main").TextContent, StringComparison.Ordinal);
            Assert.Equal("256", cut.Find("#employee-create-email").GetAttribute("maxlength"));
            Assert.NotEmpty(cut.FindAll("[role='alert']"));
        });
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Creates);
        Assert.NotEmpty(cut.FindAll("form"));
    }

    private static string Email(int length)
    {
        const string suffix = "@example.invalid";
        return new string('e', length - suffix.Length) + suffix;
    }

    private static CreateEmployeeAccountRequest Model(string email) => new()
    {
        FirstName = "Synthetic",
        LastName = "Employee",
        Email = email,
        Password = "synthetic-password",
        ConfirmPassword = "synthetic-password",
        PhoneNumber = "+66812345678",
    };

    private static async Task FillAsync(IRenderedComponent<Router> cut, string email)
    {
        var model = Model(email);
        foreach (var (id, value) in new[]
        {
            ("employee-create-first-name", model.FirstName),
            ("employee-create-last-name", model.LastName),
            ("employee-create-email", model.Email),
            ("employee-create-password", model.Password),
            ("employee-create-confirm-password", model.ConfirmPassword),
            ("employee-create-phone", model.PhoneNumber!),
        })
        {
            var input = Assert.Single(cut.FindComponents<ShadcnInput<string>>(),
                component => component.FindAll("#" + id).Count != 0);
            await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value));
        }
    }

    private static BunitContext Context(CreationTransport handler)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Employees/Create");
        return context;
    }

    private static IRenderedComponent<Router> Render(BunitContext context) => context.Render<Router>(parameters => parameters
        .Add(router => router.AppAssembly, typeof(EmployeeCreate).Assembly)
        .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
        {
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), route);
            builder.CloseComponent();
        })));

    private sealed record SentCreate(string Json, string? Csrf, string? Authorization);

    private sealed class CreationTransport : HttpMessageHandler
    {
        public int SessionReads { get; private set; }
        public ConcurrentQueue<SentCreate> Creates { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/bff/session")
            {
                SessionReads++;
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"],
                        "controlled-create-csrf", 7, [])),
                };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/bff/employees")
            {
                Creates.Enqueue(new(await request.Content!.ReadAsStringAsync(token),
                    request.Headers.TryGetValues("X-CSRF-TOKEN", out var csrf) ? Assert.Single(csrf) : null,
                    request.Headers.Authorization?.ToString()));
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(new CreatedEmployeeAccount(42, true)) };
            }
            throw new InvalidOperationException("Unexpected employee creation component HTTP request.");
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string language) =>
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);

        public void Dispose()
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
