using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using RequestView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.View;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestDetailLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string DetailPath = "/bff/quotation-requests/41";
    private const string ReceiptPath = DetailPath + "/qualification-receipt";

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task EarlierDetailCannotReplaceOrRedirectNewerView(HttpStatusCode status)
    {
        using var fixture = new Fixture(DetailPath);
        var older = fixture.Load(41);
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            await fixture.Load(42).WaitAsync(Deadline);
            var navigation = fixture.Navigation.Uri;
            fixture.Handler.Complete(status == HttpStatusCode.OK ? Detail(41) : new HttpResponseMessage(status));
            await older.WaitAsync(Deadline);

            Assert.Equal(42, fixture.Detail?.Request.Id);
            Assert.Equal(navigation, fixture.Navigation.Uri);
            Assert.Null(fixture.Error);
            Assert.False(fixture.Loading);
            Assert.DoesNotContain(ReceiptPath, fixture.Handler.Paths);
        }
        finally
        {
            await fixture.Drain(older);
        }
    }

    [Fact]
    public async Task EarlierQualificationErrorCannotReplaceNewerView()
    {
        using var fixture = new Fixture(ReceiptPath);
        var older = fixture.Load(41);
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            await fixture.Load(42).WaitAsync(Deadline);
            var currentError = fixture.QualificationError;

            fixture.Handler.Complete(new HttpResponseMessage(HttpStatusCode.Forbidden));
            await older.WaitAsync(Deadline);

            Assert.Equal(42, fixture.Detail?.Request.Id);
            Assert.NotNull(currentError);
            Assert.Equal(currentError, fixture.QualificationError);
            Assert.Null(fixture.Error);
            Assert.False(fixture.Loading);
        }
        finally
        {
            await fixture.Drain(older);
        }
    }

    [Fact]
    public async Task EarlierSessionCannotStartDetailOrClearNewerLoading()
    {
        using var fixture = new Fixture("/bff/session", holdNewDetail: true);
        var older = fixture.Load(41);
        Task? newer = null;
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            newer = fixture.Load(42);
            await fixture.Handler.NewDetailStarted.Task.WaitAsync(Deadline);

            fixture.Handler.Complete(Session());
            await older.WaitAsync(Deadline);

            Assert.Equal(new[] { "/bff/session", "/bff/session", "/bff/quotation-requests/42" }, fixture.Handler.Paths);
            Assert.True(fixture.Loading);
            Assert.Null(fixture.Error);

            fixture.Handler.CompleteNewDetail();
            await newer.WaitAsync(Deadline);
            Assert.Equal(42, fixture.Detail?.Request.Id);
            Assert.False(fixture.Loading);
        }
        finally
        {
            if (newer is not null)
            {
                fixture.Handler.CompleteNewDetail();
                await newer.WaitAsync(Deadline);
            }
            await fixture.Drain(older);
        }
    }

    [Fact]
    public async Task LateUnauthorizedAfterDisposalCannotRedirect()
    {
        using var fixture = new Fixture(DetailPath);
        var pending = fixture.Load(41);
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            var navigation = fixture.Navigation.Uri;
            fixture.Page.Dispose();

            fixture.Handler.Complete(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(Deadline);

            Assert.Equal(navigation, fixture.Navigation.Uri);
            Assert.Null(fixture.Error);
            Assert.Null(fixture.Detail);
        }
        finally
        {
            await fixture.Drain(pending);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MissingOrNonPositiveIdRetiresEarlierViewWithoutServiceCalls(int? id)
    {
        using var fixture = new Fixture();
        await fixture.Load(42).WaitAsync(Deadline);
        Assert.Equal(42, fixture.Detail?.Request.Id);
        var priorPaths = fixture.Handler.Paths;

        await fixture.Load(id).WaitAsync(Deadline);

        Assert.Equal(priorPaths, fixture.Handler.Paths);
        Assert.Null(fixture.Detail);
        Assert.NotNull(fixture.Error);
        Assert.False(fixture.Loading);
    }

    private static HttpResponseMessage Detail(int id) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { request = new { id }, files = Array.Empty<object>() }),
        };

    private static HttpResponseMessage Session() =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { isAuthenticated = true }),
        };

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly BunitContext context = new();
        private readonly HttpClient http;

        public Fixture(string? heldPath = null, bool holdNewDetail = false)
        {
            context.Services.AddLocalization();
            Navigation = context.Services.GetRequiredService<NavigationManager>();
            Handler = new Handler(heldPath, holdNewDetail);
            http = new HttpClient(Handler) { BaseAddress = new Uri("https://localhost") };
            Set("Http", http);
            Set("Navigation", Navigation);
            Set("Text", context.Services.GetRequiredService<IStringLocalizer<RequestView>>());
        }

        public RequestView Page { get; } = new();
        public NavigationManager Navigation { get; }
        public Handler Handler { get; }
        public QuotationRequestDetail? Detail => Get<QuotationRequestDetail?>("detail");
        public string? Error => Get<string?>("error");
        public string? QualificationError => Get<string?>("qualificationError");
        public bool Loading => Get<bool>("loading");

        public Task Load(int? id)
        {
            Navigation.NavigateTo(id is null ? "/QuotationRequests/View" : $"/QuotationRequests/View?id={id}");
            typeof(RequestView).GetProperty(nameof(RequestView.Id))!.SetValue(Page, id);
            return (Task)typeof(RequestView).GetMethod("LoadAsync", Members)!.Invoke(Page, null)!;
        }

        public async Task Drain(Task load)
        {
            Handler.Complete(new HttpResponseMessage(HttpStatusCode.NotFound));
            await load.WaitAsync(Deadline);
        }

        public void Dispose()
        {
            Page.Dispose();
            http.Dispose();
            context.Dispose();
        }

        private T Get<T>(string field) => (T)typeof(RequestView).GetField(field, Members)!.GetValue(Page)!;
        private void Set(string property, object value) => typeof(RequestView).GetProperty(property, Members)!.SetValue(Page, value);
    }

    private sealed class Handler(string? heldPath, bool holdNewDetail) : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> paths = new();
        private readonly TaskCompletionSource<HttpResponseMessage> older = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> newer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource NewDetailStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string[] Paths => paths.ToArray();

        public void Complete(HttpResponseMessage response)
        {
            if (!older.TrySetResult(response)) response.Dispose();
        }

        public void CompleteNewDetail()
        {
            var response = Detail(42);
            if (!newer.TrySetResult(response)) response.Dispose();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            paths.Enqueue(path);
            if (path == heldPath && Started.TrySetResult()) return older.Task;
            if (path == "/bff/quotation-requests/42" && holdNewDetail)
            {
                NewDetailStarted.TrySetResult();
                return newer.Task;
            }

            return Task.FromResult(path switch
            {
                "/bff/session" => Session(),
                DetailPath => Detail(41),
                "/bff/quotation-requests/42" => Detail(42),
                "/bff/quotation-requests/42/qualification-receipt" => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => throw new InvalidOperationException($"Unexpected synthetic GET {path}"),
            });
        }
    }
}
