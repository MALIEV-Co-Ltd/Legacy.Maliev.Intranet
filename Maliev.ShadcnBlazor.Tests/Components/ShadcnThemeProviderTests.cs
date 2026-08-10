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

    [Fact]
    public void UsesConfiguredDefaultsWhenParametersAreOmitted()
    {
        Services.Configure<ShadcnOptions>(options =>
        {
            options.DefaultDarkMode = true;
            options.DefaultDirection = ShadcnDirection.RightToLeft;
            options.FontFamily = "Noto Sans Thai, sans-serif";
        });

        var cut = Render<ShadcnThemeProvider>();

        var root = cut.Find("[data-shadcn-scope]");
        Assert.Equal("dark", root.GetAttribute("data-shadcn-theme"));
        Assert.Equal("rtl", root.GetAttribute("dir"));
        Assert.Equal("--shadcn-font-sans: Noto Sans Thai, sans-serif", root.GetAttribute("style"));
        var mudThemeProvider = cut.FindComponent<MudThemeProvider>().Instance;
        Assert.True(mudThemeProvider.IsDarkMode);
        var theme = Assert.IsType<MudTheme>(mudThemeProvider.Theme);
        var fontFamily = Assert.IsType<string[]>(theme.Typography.Default.FontFamily);
        Assert.Equal(["Noto Sans Thai, sans-serif"], fontFamily);
        Assert.True(cut.FindComponent<MudSnackbarProvider>().Instance.RightToLeft);
    }

    [Fact]
    public void ExplicitParametersOverrideConfiguredDefaults()
    {
        Services.Configure<ShadcnOptions>(options =>
        {
            options.DefaultDarkMode = true;
            options.DefaultDirection = ShadcnDirection.RightToLeft;
        });

        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.IsDarkMode, false)
            .Add(x => x.Direction, ShadcnDirection.LeftToRight));

        var root = cut.Find("[data-shadcn-scope]");
        Assert.Equal("light", root.GetAttribute("data-shadcn-theme"));
        Assert.Equal("ltr", root.GetAttribute("dir"));
    }

    [Fact]
    public void PreservesConsumerClassAndStyleWhileContractAttributesWin()
    {
        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.Class, "consumer-shell")
            .Add(x => x.Style, "background: black")
            .Add(x => x.AdditionalAttributes, new Dictionary<string, object>
            {
                ["class"] = "dictionary-shell",
                ["style"] = "color: rebeccapurple; --shadcn-font-sans: defeated",
                ["data-shadcn-scope"] = "false",
                ["data-shadcn-theme"] = "defeated",
                ["dir"] = "auto",
                ["aria-label"] = "consumer label"
            }));

        var root = cut.Find("[data-shadcn-scope]");
        Assert.Contains(ShadcnCss.ScopeClass, root.ClassList);
        Assert.Contains("consumer-shell", root.ClassList);
        Assert.Contains("dictionary-shell", root.ClassList);
        Assert.Equal(string.Empty, root.GetAttribute("data-shadcn-scope"));
        Assert.Equal("light", root.GetAttribute("data-shadcn-theme"));
        Assert.Equal("ltr", root.GetAttribute("dir"));
        Assert.Equal("consumer label", root.GetAttribute("aria-label"));
        Assert.Equal(
            "background: black; color: rebeccapurple; --shadcn-font-sans: defeated; --shadcn-font-sans: ui-sans-serif, system-ui, sans-serif",
            root.GetAttribute("style"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUnknownEffectiveDirection(bool explicitParameter)
    {
        var unknown = (ShadcnDirection)999;
        if (!explicitParameter)
            Services.Configure<ShadcnOptions>(options => options.DefaultDirection = unknown);

        var exception = Assert.ThrowsAny<Exception>(() => Render<ShadcnThemeProvider>(parameters =>
        {
            if (explicitParameter)
                parameters.Add(x => x.Direction, unknown);
        }));

        Assert.Contains("direction", exception.ToString(), StringComparison.OrdinalIgnoreCase);
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
