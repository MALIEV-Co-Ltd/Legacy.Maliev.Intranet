using System.Text.RegularExpressions;
using Maliev.ShadcnBlazor.Adapters.MudBlazor;
using MudBlazor;

namespace Maliev.ShadcnBlazor.Tests.Adapters.MudBlazor;

public sealed class ShadcnMudChartOptionsTests
{
    private static readonly string[] ExpectedPalette =
    [
        "var(--shadcn-chart-1)",
        "var(--shadcn-chart-2)",
        "var(--shadcn-chart-3)",
        "var(--shadcn-chart-4)",
        "var(--shadcn-chart-5)"
    ];

    [Fact]
    public void CreateReturnsAFreshMudBlazorChartPaletteForEveryChart()
    {
        var first = ShadcnMudChartOptions.Create();
        var second = ShadcnMudChartOptions.Create();

        Assert.IsType<ChartOptions>(first);
        Assert.IsType<ChartOptions>(second);
        Assert.Equal(ExpectedPalette, first.ChartPalette);
        Assert.Equal(ExpectedPalette, second.ChartPalette);
        Assert.NotSame(first, second);
        Assert.NotSame(first.ChartPalette, second.ChartPalette);

        first.ChartPalette[0] = "mutated";
        Assert.Equal("var(--shadcn-chart-1)", second.ChartPalette[0]);
    }

    [Theory]
    [InlineData("Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor", 3)]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor", 1)]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor", 2)]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", 1)]
    public void EveryMudChartConsumerPassesTheSemanticChartOptionsParameter(string relativePath, int expectedCount)
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), relativePath));
        var chartTags = Regex.Matches(source, @"<MudChart\b[^>]*>", RegexOptions.Singleline);

        Assert.Equal(expectedCount, chartTags.Count);
        Assert.All(chartTags.Cast<Match>(), chart =>
            Assert.Contains("ChartOptions=\"@ShadcnMudChartOptions.Create()\"", chart.Value, StringComparison.Ordinal));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
