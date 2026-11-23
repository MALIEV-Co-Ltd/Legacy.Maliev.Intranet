using System.Net;
using System.Globalization;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Procurement.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierCreateLifetimeTests
{
    [Fact]
    public async Task CountrySelector_RendersChoicesAndOptionalSelection()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<SupplierCreate>();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindComponent<ShadcnSelect<int>>().Instance.Options, option => option.Value == 66 && option.Text == "Thailand"));
        var select = cut.FindComponent<ShadcnSelect<int>>();
        Assert.False(select.Instance.Required);
        Assert.NotNull(select.Instance.ValueExpression);
        var model = (SupplierCreateRequest)Field(cut.Instance, "model")!;
        model.Name = "Example";
        var editContext = (EditContext)Field(cut.Instance, "editContext")!;
        await cut.InvokeAsync(async () => { await editContext.ValidateAsync(); });
        var countryField = new FieldIdentifier(model, nameof(model.CountryId));
        Assert.Empty(editContext.GetValidationMessages(countryField));
        model.Address1 = "1 Main Road";
        await cut.InvokeAsync(async () => { await editContext.ValidateAsync(); });
        cut.Render();
        Assert.True(cut.FindComponent<ShadcnSelect<int>>().Instance.Required);
        var error = Assert.Single(editContext.GetValidationMessages(countryField));
        Assert.Contains(error, cut.Markup, StringComparison.Ordinal);
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(66));
        await cut.InvokeAsync(async () => { await editContext.ValidateAsync(); });
        Assert.Equal(66, model.CountryId);
        Assert.Empty(editContext.GetValidationMessages(countryField));
    }

    [Fact]
    public async Task CountryLookup_LoadsNamesAndRetriesAfterFailure()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CountryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        try
        {
            await LoadCountries(page);
            Assert.NotNull(Field(page, "countryError"));
            await LoadCountries(page);
            var countries = Assert.IsAssignableFrom<IReadOnlyList<EmployeeAdministrationCountry>>(Field(page, "countries"));
            Assert.Equal("Thailand", Assert.Single(countries).Name);
            Assert.Null(Field(page, "countryError"));
            Assert.Equal(2, handler.Calls);
        }
        finally { page.Dispose(); }
    }

    private static Task LoadCountries(SupplierCreate page) => (Task)typeof(SupplierCreate).GetMethod("LoadCountriesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class CountryHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/bff/suppliers/create-countries", request.RequestUri!.AbsolutePath);
            Calls++;
            return Task.FromResult(Calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("[{\"id\":66,\"name\":\"Thailand\"}]") );
        }
    }

    [Theory]
    [InlineData("en", HttpStatusCode.Forbidden, "The request returned status 403. The server refused the supplier request.", "The supplier may have been created. Check the supplier list before trying again.")]
    [InlineData("en", HttpStatusCode.Conflict, "The request returned status 409. The supplier request conflicted with existing data.", "The supplier may have been created. Check the supplier list before trying again.")]
    [InlineData("en", HttpStatusCode.TooManyRequests, "The request returned status 429. The server is limiting requests.", "The supplier may have been created. Check the supplier list before trying again.")]
    [InlineData("en", HttpStatusCode.InternalServerError, "The request returned status 500.", "The supplier may have been created. Check the supplier list before trying again.")]
    [InlineData("th", HttpStatusCode.Forbidden, "คำขอส่งคืนสถานะ 403 เซิร์ฟเวอร์ปฏิเสธคำขอเพิ่มผู้จัดจำหน่าย", "อาจสร้างผู้จัดจำหน่ายแล้ว โปรดตรวจสอบรายชื่อก่อนลองอีกครั้ง")]
    [InlineData("th", HttpStatusCode.Conflict, "คำขอส่งคืนสถานะ 409 คำขอเพิ่มผู้จัดจำหน่ายขัดแย้งกับข้อมูลที่มีอยู่", "อาจสร้างผู้จัดจำหน่ายแล้ว โปรดตรวจสอบรายชื่อก่อนลองอีกครั้ง")]
    [InlineData("th", HttpStatusCode.TooManyRequests, "คำขอส่งคืนสถานะ 429 เซิร์ฟเวอร์กำลังจำกัดจำนวนคำขอ", "อาจสร้างผู้จัดจำหน่ายแล้ว โปรดตรวจสอบรายชื่อก่อนลองอีกครั้ง")]
    [InlineData("th", HttpStatusCode.InternalServerError, "คำขอส่งคืนสถานะ 500", "อาจสร้างผู้จัดจำหน่ายแล้ว โปรดตรวจสอบรายชื่อก่อนลองอีกครั้ง")]
    public async Task UncertainStatusRendersNeutralLocalizedMessage(string culture, HttpStatusCode status, string expected, string warning)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var context = new BunitContext();
            context.Services.AddLocalization();
            context.Services.AddLogging();
            using var handler = new CreateHandler(holdSession: false);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            context.Services.AddSingleton(http);
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            var page = context.Render<SupplierCreate>();
            var pending = page.InvokeAsync(() => Submit(page.Instance));
            try
            {
                await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                handler.Release.TrySetResult(new HttpResponseMessage(status));
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                page.Render();
                Assert.Contains(expected, page.Find("main").TextContent, StringComparison.Ordinal);
                Assert.Contains(warning, page.Find("main").TextContent, StringComparison.Ordinal);
                Assert.True((bool)Field(page.Instance, "outcomeUnknown")!);
            }
            finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; CultureInfo.CurrentUICulture = previousUiCulture; }
    }

    [Fact]
    public async Task ChangedInputDuringPreflightCannotDispatch()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ((SupplierCreateRequest)Field(page, "model")!).Name = "changed";
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedDuringPreflightDoesNotCreateOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uri = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
            Assert.Equal(uri, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task DisposedAfterDispatchDoesNotNavigateOrRepeat()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uri = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"id\":42}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.Equal(uri, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AmbiguousResponseFencesAnotherCreate(HttpStatusCode status)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(new HttpResponseMessage(status));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.True((bool)Field(page, "outcomeUnknown")!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task UnexpectedPostDispatchExceptionFencesRetry()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetException(new InvalidOperationException("synthetic failure"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.True((bool)Field(page, "outcomeUnknown")!);
        }
        finally { page.Dispose(); }
    }

    [Fact]
    public async Task DisposedPostDispatchExceptionIsObservedWithoutLateStateOrReplay()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetException(new InvalidOperationException("synthetic failure"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.Null(Field(page, "error"));
        }
        finally { page.Dispose(); }
    }

    private static SupplierCreate CreatePage(BunitContext context, HttpClient http)
    {
        var page = new SupplierCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<SupplierCreate>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        Set(page, "Logger", NullLogger<SupplierCreate>.Instance);
        typeof(SupplierCreate).GetField("editContext", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new EditContext(Field(page, "model")!));
        return page;
    }
    private static object? Field(SupplierCreate page, string name) => typeof(SupplierCreate).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void Set(SupplierCreate page, string name, object value) => typeof(SupplierCreate).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Submit(SupplierCreate page) => (Task)typeof(SupplierCreate).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Session() => Json("{\"isAuthenticated\":true,\"roles\":[],\"csrfToken\":\"synthetic\"}");

    private sealed class CreateHandler(bool holdSession) : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/suppliers/create-countries") return Task.FromResult(Json("[{\"id\":66,\"name\":\"Thailand\"}]"));
            if (request.Method == HttpMethod.Get)
            {
                if (!holdSession) return Task.FromResult(Session());
                Started.TrySetResult();
                return Release.Task;
            }
            Writes++;
            Started.TrySetResult();
            return Release.Task;
        }
    }
}
