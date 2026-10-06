using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Client.Shared.Infrastructure;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LookupBehaviorTests
{
    [Fact]
    public async Task SearchDebouncesAndOnlyAcceptsLatestInput()
    {
        var clock = new FakeTimeProvider();
        using var controller = new LookupSearchController<string>(clock);
        var accepted = new List<string>();
        var called = new List<string>();
        var first = controller.SearchAsync(_ => Search("old"), accepted.Add, _ => Assert.Fail("Unexpected error"));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var second = controller.SearchAsync(_ => Search("new"), accepted.Add, _ => Assert.Fail("Unexpected error"));
        clock.Advance(TimeSpan.FromMilliseconds(349));
        Assert.Empty(called);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { "new" }, called);
        Assert.Equal(new[] { "new" }, accepted);
        Task<string> Search(string value) { called.Add(value); return Task.FromResult(value); }
    }

    [Fact]
    public async Task CancellationIgnoringProviderCannotReplaceNewerSelectionOrReportStaleFailure()
    {
        using var controller = new LookupSearchController<string>();
        var old = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new List<string>();
        var errors = new List<Exception>();
        var first = controller.SearchAsync(_ => { started.SetResult(); return old.Task; }, accepted.Add, errors.Add, TimeSpan.Zero);
        await started.Task;
        await controller.SearchAsync(_ => Task.FromResult("new"), accepted.Add, errors.Add, TimeSpan.Zero);
        old.SetException(new LookupRequestException(HttpStatusCode.ServiceUnavailable));
        await first;
        Assert.Equal(new[] { "new" }, accepted);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ParentChangeOrDisposalRejectsLateResults()
    {
        using var controller = new LookupSearchController<string>();
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new List<string>();
        var pending = controller.SearchAsync(_ => { started.SetResult(); return result.Task; }, accepted.Add, _ => Assert.Fail(), TimeSpan.Zero);
        await started.Task;
        controller.Invalidate();
        controller.Dispose();
        result.SetResult("late");
        await pending;
        Assert.Empty(accepted);
    }

    [Fact]
    public void ProvinceChangeClearsChildrenButPreservesSupplierDetailAndCountry()
    {
        var target = new SupplierCreateRequest { Building = "Building", Address1 = "Street", Address2 = "Floor", CountryId = 66 };
        var selected = LookupAddressSelection.From(Combination);
        LookupSupplierSuggestions.ApplyAddress(target, selected);
        Assert.Equal("คลองเตย, คลองเตย", target.City);
        Assert.Equal("กรุงเทพมหานคร", target.State);
        Assert.Equal("10110", target.PostalCode);
        var changed = selected.WithProvince(new("50", "เชียงใหม่", "Chiang Mai"));
        LookupSupplierSuggestions.ApplyAddress(target, changed);
        Assert.Null(changed.District);
        Assert.Null(changed.Subdistrict);
        Assert.Null(target.PostalCode);
        Assert.Equal(string.Empty, target.City);
        Assert.Equal("Street", target.Address1);
        Assert.Equal("Floor", target.Address2);
        Assert.Equal("Building", target.Building);
        Assert.Equal(66, target.CountryId);
        Assert.Throws<ArgumentException>(() => changed.WithDistrict(Combination.District));
    }

    [Fact]
    public void CompanySuggestionDoesNotInventOrClearMissingDetails()
    {
        var target = new SupplierCreateRequest { Name = "Manual", TaxNumber = "existing", Website = "https://manual.test", Address1 = "Street", CountryId = 1 };
        LookupSupplierSuggestions.ApplyCompany(target, new("บริษัท ทดสอบ", null, null, null, null, null, null, null, null), true);
        Assert.Equal("บริษัท ทดสอบ", target.Name);
        Assert.Equal("existing", target.TaxNumber);
        Assert.Equal("https://manual.test", target.Website);
        Assert.Equal("Street", target.Address1);
        Assert.Equal(1, target.CountryId);
    }

    [Fact]
    public async Task SameOriginClientPreservesAllFiltersAndDecodesParentCodeAndLeadingZeros()
    {
        using var handler = new Handler(request => new(HttpStatusCode.OK) { Content = JsonContent.Create(new LookupAddressPage("v1", [Combination], true, "opaque+cursor")) });
        using var http = new HttpClient(handler) { BaseAddress = new("https://intranet.test") };
        var result = await new LookupClient(http).SearchAddressesAsync("คลองเตย", new("10", "1033", "103301", "10110"), "a+b", CancellationToken.None);
        Assert.Contains("provinceCode=10&districtCode=1033&subdistrictCode=103301&postcode=10110&cursor=a%2Bb", handler.Path);
        Assert.Equal("1033", result.Items[0].Subdistrict.ParentCode);
        Assert.Equal("opaque+cursor", result.NextCursor);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task PasteUsesSessionCsrfAndRetainsOriginalTextWithoutErrorBodyLeak()
    {
        string? csrf = null;
        string? body = null;
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session") return new(HttpStatusCode.OK)
                { Content = JsonContent.Create(new EmployeeSessionSummary(true, "staff", "Staff", ["Employee"], "csrf")) };
            csrf = request.Headers.GetValues("X-CSRF-TOKEN").Single();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private-provider-body") };
        });
        using var http = new HttpClient(handler) { BaseAddress = new("https://intranet.test") };
        var error = await Assert.ThrowsAsync<LookupRequestException>(() => new LookupClient(http)
            .ResolveAsync(new("1 ถนนสุขุมวิท เขตคลองเตย ๑๐๑๑๐"), CancellationToken.None));
        Assert.Equal("csrf", csrf);
        Assert.Contains("text", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.DoesNotContain("private-provider-body", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CompanyFailureIsExplicitInsteadOfEmptyMatch(HttpStatusCode status)
    {
        using var handler = new Handler(_ => new(status) { Content = new StringContent("private-provider-body") });
        using var http = new HttpClient(handler) { BaseAddress = new("https://intranet.test") };
        var error = await Assert.ThrowsAsync<LookupRequestException>(() => new LookupClient(http)
            .SearchCompaniesAsync("test", false, "en", CancellationToken.None));
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("private-provider-body", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"datasetVersion\":\"v1\",\"items\":null,\"hasMore\":false}")]
    [InlineData("{\"datasetVersion\":\"v1\",\"items\":[],\"hasMore\":true,\"nextCursor\":null}")]
    public async Task MalformedSuccessCannotMasqueradeAsEmptyLookup(string json)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        using var http = new HttpClient(handler) { BaseAddress = new("https://intranet.test") };
        var error = await Assert.ThrowsAsync<LookupRequestException>(() => new LookupClient(http)
            .SearchAddressesAsync("", new(), null, CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    }

    internal static readonly LookupAddressCombination Combination = new(new("10", "กรุงเทพมหานคร", "Bangkok"),
        new("1033", "คลองเตย", "Khlong Toei", "10"), new("103301", "คลองเตย", "Khlong Toei", "1033"), "10110");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Path = request.RequestUri?.PathAndQuery; return Task.FromResult(response(request)); }
    }
}
