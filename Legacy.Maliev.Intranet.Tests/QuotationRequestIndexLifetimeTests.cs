using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using RequestIndex = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.Index;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestIndexLifetimeTests
{
    [Fact]
    public async Task EarlierFailedSearchCannotReplaceNewerSuccessfulResults()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DeferredHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new RequestIndex();
        Set(page, "Http", http);
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<RequestIndex>>());
        var older = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Search = "new";
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Older.TrySetException(new HttpRequestException("synthetic failure"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(typeof(RequestIndex).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.NotNull(typeof(RequestIndex).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally
        {
            handler.Older.TrySetCanceled();
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    private static void Set(RequestIndex page, string property, object value) =>
        typeof(RequestIndex).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private static Task Load(RequestIndex page) =>
        (Task)typeof(RequestIndex).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class DeferredHandler : HttpMessageHandler
    {
        private int calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Older { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Started.TrySetResult();
                return Older.Task;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = Array.Empty<object>(), pageIndex = 1, totalPages = 0, totalRecords = 0, hasNextPage = false, hasPreviousPage = false }),
            });
        }
    }
}
