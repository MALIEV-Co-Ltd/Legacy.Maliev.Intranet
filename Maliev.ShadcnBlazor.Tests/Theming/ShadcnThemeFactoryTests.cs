using Maliev.ShadcnBlazor.Theming;
using MudBlazor.Utilities;

namespace Maliev.ShadcnBlazor.Tests.Theming;

public sealed class ShadcnThemeFactoryTests
{
    [Fact]
    public void CreateMapsPinnedNeutralPalettesAndConsumerFont()
    {
        var theme = ShadcnThemeFactory.Create(new ShadcnOptions
        {
            FontFamily = "IBM Plex Sans Thai, sans-serif"
        });

        Assert.Equal(new MudColor("#171717"), theme.PaletteLight.Primary);
        Assert.Equal(new MudColor("#ffffff"), theme.PaletteLight.Background);
        Assert.Equal(new MudColor("#e4e4e7"), theme.PaletteDark.Primary);
        Assert.Equal(new MudColor("#252525"), theme.PaletteDark.Background);
        Assert.Equal("IBM Plex Sans Thai, sans-serif", theme.Typography.Default.FontFamily!.Single());
        Assert.Equal("IBM Plex Sans Thai, sans-serif", theme.Typography.Button.FontFamily!.Single());
    }
}
