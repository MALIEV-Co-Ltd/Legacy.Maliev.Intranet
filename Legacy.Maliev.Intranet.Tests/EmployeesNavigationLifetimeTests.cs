using System.Reflection;
using EmployeesPage = Legacy.Maliev.Intranet.Client.Features.Employees.Pages.Employees;
using Maliev.ShadcnBlazor.Components.DataDisplay;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeesNavigationLifetimeTests
{
    [Fact]
    public async Task DisposedWhileUrlUpdateIsPending_DoesNotStartAnotherLoad()
    {
        var js = new DeferredNavigationRuntime();
        var page = new EmployeesPage();
        typeof(EmployeesPage).GetProperty("JavaScript", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, js.Runtime);
        var request = new ShadcnDataTableRequest(string.Empty, [], new Dictionary<string, string>(),
            new HashSet<string>(StringComparer.Ordinal), 0, 25);
        var pending = (Task)typeof(EmployeesPage).GetMethod("HandleDataTableRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [request])!;

        page.Dispose();
        js.Complete();

        await pending;
    }

    private sealed class DeferredNavigationRuntime
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IJSRuntime Runtime => new Adapter(this);

        public void Complete() => completion.SetResult();
        private async ValueTask<TValue> InvokeCore<TValue>(string identifier)
        {
            Assert.Equal("malievNavigation.replaceCurrentUrl", identifier);
            await completion.Task;
            return default!;
        }

        private sealed class Adapter(DeferredNavigationRuntime owner) : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => owner.InvokeCore<TValue>(identifier);
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => owner.InvokeCore<TValue>(identifier);
        }
    }
}
