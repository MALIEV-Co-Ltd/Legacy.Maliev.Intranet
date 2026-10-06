using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Shared.Components;
using Legacy.Maliev.Intranet.Client.Shared.Infrastructure;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LookupComponentTests : BunitContext
{
    public LookupComponentTests()
    {
        Services.AddLocalization();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void NonThaiManualAddressDoesNotLaunchLookupRequests()
    {
        using var handler = new Handler();
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://intranet.test") });
        var cut = Render<LookupAddressAssist>(parameters => parameters.Add(x => x.Id, "address").Add(x => x.CountryKey, "foreign"));
        Assert.Empty(cut.FindAll("input[type=text]"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task UnavailableSearchKeepsManualEntryAndAccessibleStatus()
    {
        var cut = Render<LookupSearchField<string>>(parameters => parameters
            .Add(x => x.Id, "company")
            .Add(x => x.Label, "Company")
            .Add(x => x.Display, x => x)
            .Add(x => x.MinimumLength, 2)
            .Add(x => x.Search, (_, _, _) => Task.FromException<LookupSearchPage<string>>(new LookupRequestException(HttpStatusCode.ServiceUnavailable))));
        await cut.InvokeAsync(() => cut.FindComponent<ShadcnInput<string>>().Instance.ValueChanged.InvokeAsync("test"));
        cut.WaitForAssertion(() => Assert.Contains("unavailable", cut.Find("[role=status]").TextContent));
        Assert.False(cut.Find("input").HasAttribute("disabled"));
        Assert.Equal("company", cut.Find("label").GetAttribute("for"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LookupAddressPage("v1", [], false, null)) });
        }
    }
}
