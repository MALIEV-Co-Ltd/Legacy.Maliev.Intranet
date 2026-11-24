using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Quotations.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationCustomerLookupTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string SearchBox = "#quotation-customer-search";

    [Fact]
    public async Task SearchBoundsAndEncodesQueryThenSelectsThroughCallback()
    {
        using var fixture = new Fixture(_ => Task.FromResult(Customers(41, 12)));
        fixture.Component.Find(SearchBox).Input(new string('X', 101));
        await fixture.SearchAsync().WaitAsync(Deadline);
        Assert.Empty(fixture.Handler.Queries);

        fixture.Component.Find(SearchBox).Input("  A+B & /ไทย  ");
        await fixture.SearchAsync().WaitAsync(Deadline);

        Assert.Equal("?search=A%2BB%20%26%20%2F%E0%B9%84%E0%B8%97%E0%B8%A2&index=1&size=10",
            Assert.Single(fixture.Handler.Queries));
        var choices = fixture.Component.FindAll("li button");
        Assert.Equal(10, choices.Count);
        Assert.Contains("Synthetic 41", choices[0].TextContent, StringComparison.Ordinal);
        await choices[0].ClickAsync(new MouseEventArgs()).WaitAsync(Deadline);
        Assert.Equal(41, Assert.Single(fixture.Selections));
        Assert.Empty(fixture.Component.FindAll("li button"));
        Assert.Single(fixture.Handler.Queries);
    }

    [Fact]
    public async Task ObsoleteRequestCannotPublishAfterNewerSearch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(query =>
        {
            if (query == "?search=older&index=1&size=10")
            {
                started.TrySetResult();
                return delayed.Task;
            }

            Assert.Equal("?search=current&index=1&size=10", query);
            return Task.FromResult(Customers(42, 1));
        });
        fixture.Component.Find(SearchBox).Input("older");
        var older = fixture.SearchAsync();
        try
        {
            await started.Task.WaitAsync(Deadline);
            fixture.Component.Find(SearchBox).Input("current");
            await fixture.SearchAsync().WaitAsync(Deadline);
            delayed.TrySetResult(Customers(41, 1));
            await older.WaitAsync(Deadline);

            var choice = Assert.Single(fixture.Component.FindAll("li button"));
            Assert.Contains("Synthetic 42", choice.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic 41", fixture.Component.Markup, StringComparison.Ordinal);
            await choice.ClickAsync(new MouseEventArgs()).WaitAsync(Deadline);
            Assert.Equal(42, Assert.Single(fixture.Selections));
            Assert.Equal(2, fixture.Handler.Queries.Count);
        }
        finally
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (!delayed.TrySetResult(response)) response.Dispose();
            await older.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task ForbiddenClearsChoicesWithoutFallbackOrSelection()
    {
        using var fixture = new Fixture(query => Task.FromResult(query == "?search=allowed&index=1&size=10"
            ? Customers(41, 1)
            : new HttpResponseMessage(HttpStatusCode.Forbidden)));
        fixture.Component.Find(SearchBox).Input("allowed");
        await fixture.SearchAsync().WaitAsync(Deadline);
        Assert.Single(fixture.Component.FindAll("li button"));

        fixture.Component.Find(SearchBox).Input("forbidden");
        await fixture.SearchAsync().WaitAsync(Deadline);

        Assert.Contains("You do not have permission to search the customer list.",
            fixture.Component.Find("[role='status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(fixture.Component.FindAll("li button"));
        Assert.Empty(fixture.Selections);
        Assert.Equal(new[] { "?search=allowed&index=1&size=10", "?search=forbidden&index=1&size=10" },
            fixture.Handler.Queries.ToArray());
    }

    private static HttpResponseMessage Customers(int firstId, int count)
    {
        var rows = Enumerable.Range(firstId, count)
            .Select(id => new CustomerListItem(id, "Synthetic", id.ToString(), $"Synthetic {id}", "", null))
            .ToArray();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CustomerListPage(rows, 1, 1, count, false, false)),
        };
    }

    private sealed class Fixture : IDisposable
    {
        private readonly BunitContext context = new();
        private readonly HttpClient http;

        public Fixture(Func<string, Task<HttpResponseMessage>> respond)
        {
            Handler = new Handler(respond);
            http = new HttpClient(Handler) { BaseAddress = new Uri("https://localhost") };
            context.Services.AddLocalization();
            context.Services.AddSingleton(http);
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            Component = context.Render<QuotationCustomerLookup>(parameters =>
                parameters.Add(child => child.CustomerSelected, (int id) => Selections.Add(id)));
        }

        public Handler Handler { get; }
        public IRenderedComponent<QuotationCustomerLookup> Component { get; }
        public List<int> Selections { get; } = [];
        public Task SearchAsync() => Component.Find("button").ClickAsync(new MouseEventArgs());

        public void Dispose()
        {
            context.Dispose();
            http.Dispose();
        }
    }

    private sealed class Handler(Func<string, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://localhost/bff/customers", request.RequestUri!.GetLeftPart(UriPartial.Path));
            Queries.Enqueue(request.RequestUri.Query);
            return respond(request.RequestUri.Query);
        }
    }
}
