using Bunit;
using Maliev.ShadcnBlazor.Components;
using Maliev.ShadcnBlazor.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

#pragma warning disable MUD0012 // Assertions observe the rendered providers' current parameter state.

namespace Maliev.ShadcnBlazor.Tests.Components;

public sealed class ShadcnThemeProviderTests : BunitContext
{
    public ShadcnThemeProviderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMalievShadcn();
    }

    [Fact]
    public void RendersScopedDarkRtlRootAndAllMudProviders()
    {
        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.IsDarkMode, true)
            .Add(x => x.Direction, ShadcnDirection.RightToLeft)
            .Add(x => x.Class, "consumer-shell")
            .AddChildContent("content"));

        var root = cut.Find("[data-shadcn-scope]");
        Assert.Contains(ShadcnCss.ScopeClass, root.ClassList);
        Assert.Contains("consumer-shell", root.ClassList);
        Assert.Equal("dark", root.GetAttribute("data-shadcn-theme"));
        Assert.Equal("rtl", root.GetAttribute("dir"));
        Assert.Equal("content", root.TextContent.Trim());
        Assert.True(cut.FindComponent<MudThemeProvider>().Instance.IsDarkMode);
        Assert.Equal(ShadcnCss.OverlayScopeClass,
            cut.FindComponent<MudDialogProvider>().Instance.BackgroundClass);
        cut.FindComponent<MudPopoverProvider>();
        Assert.True(cut.FindComponent<MudSnackbarProvider>().Instance.RightToLeft);
    }

    [Fact]
    public void CascadesTheCurrentThemeAndDirection()
    {
        ShadcnContext? observed = null;
        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.Direction, ShadcnDirection.LeftToRight)
            .AddChildContent<CaptureContext>(child => child.Add(x => x.OnCaptured, value => observed = value)));
        Assert.Equal(new ShadcnContext(false, ShadcnDirection.LeftToRight), observed);
    }

    private sealed class CaptureContext : ComponentBase
    {
        [CascadingParameter] public ShadcnContext Context { get; set; }
        [Parameter] public Action<ShadcnContext>? OnCaptured { get; set; }
        protected override void OnParametersSet() => OnCaptured?.Invoke(Context);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeAsyncCore().AsTask().GetAwaiter().GetResult();

        base.Dispose(disposing);
    }
}
