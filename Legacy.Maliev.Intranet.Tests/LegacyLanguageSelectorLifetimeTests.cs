using System.Reflection;
using Legacy.Maliev.Intranet.Client.Components.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyLanguageSelectorLifetimeTests
{
    [Fact]
    public async Task CultureChangeCompletingAfterDisposalDoesNotNavigate()
    {
        var runtime = new DeferredJsRuntime();
        var navigation = new RecordingNavigationManager();
        var selector = new LegacyLanguageSelector();
        typeof(LegacyLanguageSelector).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(selector, runtime.Runtime);
        typeof(LegacyLanguageSelector).GetProperty("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(selector, navigation);
        var pending = (Task)typeof(LegacyLanguageSelector).GetMethod("ChangeCultureAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(selector, ["th-TH"])!;

        selector.Dispose();
        runtime.Complete();
        await pending;

        Assert.Equal(0, navigation.Navigations);
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public int Navigations { get; private set; }
        public RecordingNavigationManager() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => Navigations++;
    }

    private sealed class DeferredJsRuntime
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IJSRuntime Runtime => new Adapter(this);
        public void Complete() => completion.SetResult();
        private ValueTask<TValue> InvokeCore<TValue>(string identifier)
        {
            Assert.Equal("malievCulture.set", identifier);
            return new(Wait<TValue>());
        }
        private async Task<TValue> Wait<TValue>() { await completion.Task; return default!; }

        private sealed class Adapter(DeferredJsRuntime owner) : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => owner.InvokeCore<TValue>(identifier);
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => owner.InvokeCore<TValue>(identifier);
        }
    }
}
