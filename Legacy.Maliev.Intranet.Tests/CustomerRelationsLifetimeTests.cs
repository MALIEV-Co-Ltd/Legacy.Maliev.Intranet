using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerRelationsLifetimeTests
{
    [Fact]
    public async Task ChangedCustomerWhileProfileReadIsPendingCannotStartOldRelationRead()
    {
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        using var context = CreateContext(http);
        var cut = CreatePage(context);
        var page = cut.Instance;
        var pending = Invoke(page, "BeginAsync", "company");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.Render(parameters => parameters.Add(component => component.Customer, Customer(43)));
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Requests);
            Assert.Null(Field(page, "editContext"));
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ChangedCompanyWhileSessionReadIsPendingCannotSendWrite()
    {
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        using var context = CreateContext(http);
        var page = CreatePage(context).Instance;
        var company = (CustomerCompanyEdit)Field(page, "company")!;
        company.Name = "Before";
        SetField(page, "kind", "company");
        SetField(page, "relation", "new");
        SetField(page, "version", "version");
        SetField(page, "customerVersion", "version");
        SetField(page, "editContext", new EditContext(company));
        var pending = Invoke(page, "SaveAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            company.Name = "After";
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic", 7, [])) });
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Requests);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainWriteCannotBeRetriedByReopeningTheRelation(bool throwUnexpected)
    {
        using var handler = new UncertainHandler(throwUnexpected);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        using var context = CreateContext(http);
        var page = CreatePage(context).Instance;
        var company = (CustomerCompanyEdit)Field(page, "company")!;
        company.Name = "Before";
        SetField(page, "kind", "company");
        SetField(page, "relation", "new");
        SetField(page, "version", "version");
        SetField(page, "customerVersion", "version");
        SetField(page, "editContext", new EditContext(company));
        await Invoke(page, "SaveAsync");
        await Invoke(page, "CancelAsync");
        await Invoke(page, "BeginAsync", "company");
        Assert.Equal(1, handler.Writes);
        Assert.Equal(2, handler.Requests);
        Assert.Equal("RelationOutcomeUnknown", Field(page, "error"));
    }

    [Fact]
    public async Task DisposingAfterDispatchDoesNotCancelOrReplayTheWrite()
    {
        using var handler = new HeldWriteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        using var context = CreateContext(http);
        var page = CreatePage(context).Instance;
        var company = (CustomerCompanyEdit)Field(page, "company")!;
        company.Name = "Before";
        SetField(page, "kind", "company");
        SetField(page, "relation", "new");
        SetField(page, "version", "version");
        SetField(page, "customerVersion", "version");
        SetField(page, "editContext", new EditContext(company));
        var pending = Invoke(page, "SaveAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            company.Name = "After";
            var body = await handler.WriteRequest!.Content!.ReadAsStringAsync();
            Assert.Contains("Before", body, StringComparison.Ordinal);
            Assert.DoesNotContain("After", body, StringComparison.Ordinal);
            await context.DisposeComponentsAsync();
            Assert.False(handler.WriteCancellation.IsCancellationRequested);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Writes);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task CustomerChangeDuringEditingCallbackCannotNotifySavedForNewCustomer()
    {
        using var handler = new ConfirmedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        using var context = CreateContext(http);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        var cut = CreatePage(context,
            async _ => { entered.TrySetResult(); await release.Task; },
            _ => notifications++);
        var page = cut.Instance;
        var company = (CustomerCompanyEdit)Field(page, "company")!;
        company.Name = "Before";
        SetField(page, "kind", "company");
        SetField(page, "relation", "new");
        SetField(page, "version", "version");
        SetField(page, "customerVersion", "version");
        SetField(page, "editContext", new EditContext(company));
        var pending = Invoke(page, "SaveAsync");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.Render(parameters => parameters.Add(component => component.Customer, Customer(43)));
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, notifications);
        }
        finally { release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private static BunitContext CreateContext(HttpClient http)
    {
        var context = new BunitContext();
        context.Services.AddLogging();
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<CustomerRelations> CreatePage(BunitContext context,
        Func<bool, Task>? editingChanged = null, Action<string>? saved = null) =>
        context.Render<CustomerRelations>(parameters =>
        {
            parameters.Add(component => component.Customer, Customer(42))
                .Add(component => component.Permissions, new[] { "legacy-customer.customers.update", "legacy-customer.companies.create" })
                .Add(component => component.Localize, key => key);
            if (editingChanged is not null) parameters.Add(component => component.EditingChanged, editingChanged);
            if (saved is not null) parameters.Add(component => component.Saved, saved);
        });

    private static CustomerDetail Customer(int id) => new(id, "Synthetic", "Customer", "Synthetic Customer", null, null, null,
        "synthetic@example.invalid", null, null, null, null, null, null, null, null, null);
    private static object? Field(CustomerRelations page, string name) => typeof(CustomerRelations).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void SetField(CustomerRelations page, string name, object value) => typeof(CustomerRelations).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Invoke(CustomerRelations page, string name, params object[] args) =>
        (Task)typeof(CustomerRelations).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args)!;

    private sealed class HeldHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class UncertainHandler(bool throwUnexpected) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method == HttpMethod.Put)
            {
                Writes++;
                if (throwUnexpected) return Task.FromException<HttpResponseMessage>(new NotSupportedException("synthetic"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic", 7, [])) });
        }
    }

    private sealed class HeldWriteHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public HttpRequestMessage? WriteRequest { get; private set; }
        public CancellationToken WriteCancellation { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic", 7, [])) });
            Writes++;
            WriteRequest = request;
            WriteCancellation = cancellationToken;
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class ConfirmedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic", 7, [])) });
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"" + new string('a', 64) + "\"");
            response.Headers.TryAddWithoutValidation("X-Customer-ETag", "\"12345678\"");
            response.Headers.TryAddWithoutValidation("X-Relation-Id", "8");
            return Task.FromResult(response);
        }
    }
}
