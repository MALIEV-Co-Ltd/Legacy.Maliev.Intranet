using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Materials;

namespace Legacy.Maliev.Intranet.Orders;

/// <summary>Cross-service reference data required by the order editor.</summary>
public sealed record OrderReferenceData(
    IReadOnlyList<ProcessResponse> Processes,
    IReadOnlyList<MaterialResponse> Materials,
    IReadOnlyList<ColorResponse> Colors,
    IReadOnlyList<SurfaceFinishResponse> SurfaceFinishes,
    IReadOnlyList<CurrencyResponse> Currencies,
    IReadOnlyList<EmployeeResponse> Employees)
{
    /// <summary>Empty safe fallback.</summary>
    public static OrderReferenceData Empty { get; } = new([], [], [], [], [], []);
}

/// <summary>Loads independent order editor lookups concurrently.</summary>
public sealed class OrderReferenceDataLoader(
    ILegacyOrderClient orders,
    ILegacyCatalogClient catalog,
    ILegacyEmployeeClient employees)
{
    /// <summary>Loads the bounded legacy reference data working set.</summary>
    public async Task<OrderReferenceData> LoadAsync(string token, CancellationToken cancellationToken)
    {
        var processesTask = orders.GetProcessesAsync(token, cancellationToken);
        var materialsTask = catalog.GetMaterialsAsync(MaterialSortType.MaterialGroup_Ascending, null, 1, 250, token, cancellationToken);
        var colorsTask = catalog.GetColorsAsync(token, cancellationToken);
        var finishesTask = catalog.GetSurfaceFinishesAsync(token, cancellationToken);
        var currenciesTask = catalog.GetCurrenciesAsync(token, cancellationToken);
        var employeesTask = employees.GetEmployeesAsync(EmployeeSortType.EmployeeId_Ascending, null, 1, 250, token, cancellationToken);
        await Task.WhenAll(processesTask, materialsTask, colorsTask, finishesTask, currenciesTask, employeesTask);
        return new(
            (await processesTask).OrderBy(value => value.Name).ToArray(),
            (await materialsTask)?.Items.OrderBy(value => value.MaterialGroupId).ThenBy(value => value.Name).ToArray() ?? [],
            (await colorsTask).OrderBy(value => value.Name).ToArray(),
            (await finishesTask).OrderBy(value => value.Name).ToArray(),
            (await currenciesTask).OrderBy(value => value.ShortName).ToArray(),
            (await employeesTask)?.Items.OrderBy(value => value.FullName).ToArray() ?? []);
    }
}
