using MudBlazor;

namespace Maliev.ShadcnBlazor.Adapters.MudBlazor;

/// <summary>Creates isolated MudBlazor chart options backed by the package semantic chart palette.</summary>
public static class ShadcnMudChartOptions
{
    /// <summary>Creates a new option instance so chart consumers cannot share mutable palette state.</summary>
    public static ChartOptions Create() => new()
    {
        ChartPalette =
        [
            "var(--shadcn-chart-1)",
            "var(--shadcn-chart-2)",
            "var(--shadcn-chart-3)",
            "var(--shadcn-chart-4)",
            "var(--shadcn-chart-5)"
        ]
    };
}
