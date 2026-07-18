using Legacy.Maliev.Intranet.Client.Features.Accounting;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinanceChartsWasmMigrationContractTests
{
    [Fact]
    public void ChartRoutes_AreLocalizedMudBlazorAndUseReadOnlyBffTrends()
    {
        var root = FindRoot();
        var pages = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages");
        var activityPath = Path.Combine(pages, "YearlyActivityChart.razor");
        var profitPath = Path.Combine(pages, "NetProfitChart.razor");
        var dataClientPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "FinanceChartDataClient.cs");

        Assert.True(File.Exists(activityPath));
        Assert.True(File.Exists(Path.ChangeExtension(activityPath, ".resx")));
        Assert.True(File.Exists(profitPath));
        Assert.True(File.Exists(Path.ChangeExtension(profitPath, ".resx")));
        var activity = File.ReadAllText(activityPath);
        var profit = File.ReadAllText(profitPath);
        var dataClient = File.ReadAllText(dataClientPath);
        Assert.Contains("@page \"/Finances/YearlyActivityChart\"", activity, StringComparison.Ordinal);
        Assert.Contains("@page \"/Finances/NetProfitChart\"", profit, StringComparison.Ordinal);
        Assert.Contains("FinanceChartDataClient.LoadAsync", activity, StringComparison.Ordinal);
        Assert.Contains("FinanceChartDataClient.LoadAsync", profit, StringComparison.Ordinal);
        Assert.Contains("/bff/finances/trends/yearly-income", dataClient, StringComparison.Ordinal);
        Assert.Contains("/bff/finances/trends/yearly-expense", dataClient, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery(Name = \"year\")", profit, StringComparison.Ordinal);
        Assert.Contains("MudChart", activity, StringComparison.Ordinal);
        Assert.Contains("MudChart", profit, StringComparison.Ordinal);
        Assert.DoesNotContain("<canvas", activity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Chart(", activity, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Chart(", profit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IJSRuntime", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("IJSRuntime", profit, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivitySeries_SortsDatesAndNegatesExpenseLikeLegacyChart()
    {
        var day1 = new DateTime(2030, 1, 1);
        var day2 = new DateTime(2030, 1, 2);
        var result = FinanceChartSeries.BuildActivity(
            new Dictionary<DateTime, decimal> { [day2] = 250.5m, [day1] = 100m },
            new Dictionary<DateTime, decimal> { [day1] = 40m, [day2] = 10.25m });

        Assert.Equal(["01/01", "02/01"], result.Labels);
        Assert.Equal([100d, 250.5d], result.Income);
        Assert.Equal([-40d, -10.25d], result.Expense);
    }

    [Fact]
    public void NetIncomeSeries_PreservesDailyAmountsAndCumulativeTotal()
    {
        var result = FinanceChartSeries.BuildNetIncome(new Dictionary<DateTime, decimal>
        {
            [new DateTime(2030, 2, 3)] = -25.25m,
            [new DateTime(2030, 2, 1)] = 100.50m,
            [new DateTime(2030, 2, 2)] = 50m,
        });

        Assert.Equal(["01/02", "02/02", "03/02"], result.Labels);
        Assert.Equal([100.5d, 50d, -25.25d], result.DailyAmounts);
        Assert.Equal([100.5d, 150.5d, 125.25d], result.CumulativeAmounts);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
