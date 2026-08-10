using MudBlazor;

namespace Maliev.ShadcnBlazor.Theming;

public static class ShadcnThemeFactory
{
    public static MudTheme Create(ShadcnOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fonts = new[] { options.FontFamily };
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#171717", PrimaryDarken = "#0a0a0a", PrimaryLighten = "#404040",
                Secondary = "#f5f5f5", SecondaryDarken = "#e5e5e5", SecondaryLighten = "#fafafa",
                Background = "#ffffff", Surface = "#ffffff", TextPrimary = "#171717",
                TextSecondary = "#737373", AppbarBackground = "#ffffff", AppbarText = "#171717",
                DrawerBackground = "#ffffff", DrawerText = "#171717"
            },
            PaletteDark = new PaletteDark
            {
                Primary = "#e4e4e7", PrimaryDarken = "#d4d4d8", PrimaryLighten = "#fafafa",
                Secondary = "#3f3f46", SecondaryDarken = "#27272a", SecondaryLighten = "#52525b",
                Background = "#252525", Surface = "#333333", TextPrimary = "#fafafa",
                TextSecondary = "#a3a3a3", AppbarBackground = "#333333", AppbarText = "#fafafa",
                DrawerBackground = "#333333", DrawerText = "#fafafa"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = fonts },
                H1 = new H1Typography { FontFamily = fonts },
                H2 = new H2Typography { FontFamily = fonts },
                H3 = new H3Typography { FontFamily = fonts },
                H4 = new H4Typography { FontFamily = fonts },
                H5 = new H5Typography { FontFamily = fonts },
                H6 = new H6Typography { FontFamily = fonts },
                Body1 = new Body1Typography { FontFamily = fonts },
                Body2 = new Body2Typography { FontFamily = fonts },
                Button = new ButtonTypography { FontFamily = fonts },
                Caption = new CaptionTypography { FontFamily = fonts },
                Subtitle1 = new Subtitle1Typography { FontFamily = fonts },
                Subtitle2 = new Subtitle2Typography { FontFamily = fonts }
            }
        };
    }
}
