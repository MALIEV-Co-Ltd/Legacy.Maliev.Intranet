using System.Globalization;

namespace Legacy.Maliev.Intranet.Client.Features.Accounting;

/// <summary>Chart-ready yearly income and expense series.</summary>
public sealed record FinanceActivityChartData(string[] Labels, double[] Income, double[] Expense);

/// <summary>Chart-ready daily and cumulative net-income series.</summary>
public sealed record FinanceNetIncomeChartData(string[] Labels, double[] DailyAmounts, double[] CumulativeAmounts);

/// <summary>Builds deterministic chart series from AccountingService trend values.</summary>
public static class FinanceChartSeries
{
    /// <summary>Builds chronological income and negative-expense activity series.</summary>
    public static FinanceActivityChartData BuildActivity(
        IReadOnlyDictionary<DateTime, decimal> income,
        IReadOnlyDictionary<DateTime, decimal> expense)
    {
        var dates = income.Keys.Concat(expense.Keys).Distinct().OrderBy(value => value).ToArray();
        return new(
            dates.Select(FormatDate).ToArray(),
            dates.Select(date => decimal.ToDouble(income.GetValueOrDefault(date))).ToArray(),
            dates.Select(date => -Math.Abs(decimal.ToDouble(expense.GetValueOrDefault(date)))).ToArray());
    }

    /// <summary>Builds chronological daily and cumulative income series.</summary>
    public static FinanceNetIncomeChartData BuildNetIncome(IReadOnlyDictionary<DateTime, decimal> income)
    {
        var entries = income.OrderBy(entry => entry.Key).ToArray();
        var runningTotal = 0m;
        var cumulative = new double[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            runningTotal += entries[index].Value;
            cumulative[index] = decimal.ToDouble(runningTotal);
        }

        return new(
            entries.Select(entry => FormatDate(entry.Key)).ToArray(),
            entries.Select(entry => decimal.ToDouble(entry.Value)).ToArray(),
            cumulative);
    }

    private static string FormatDate(DateTime value) => value.ToString("dd/MM", CultureInfo.InvariantCulture);
}
