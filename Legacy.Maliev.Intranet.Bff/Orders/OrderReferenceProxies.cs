using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Reads CatalogService lookup values needed only by the order editor.</summary>
public sealed class OrderCatalogReferenceProxy(HttpClient httpClient)
{
    /// <summary>Gets a bounded material lookup page.</summary>
    public async Task<IReadOnlyList<OrderLookupItem>> GetMaterialsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/Materials?sort=MaterialId_Ascending&search=&index=1&size=1000", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderMaterialPage>(cancellationToken))?.Items
            .Select(item => new OrderLookupItem(item.Id, item.Name)).ToArray() ?? [];
    }

    /// <summary>Gets color lookup values.</summary>
    public Task<IReadOnlyList<OrderLookupItem>> GetColorsAsync(CancellationToken cancellationToken) =>
        GetListAsync<OrderLookupItem>("/materials/Colors", cancellationToken);

    /// <summary>Gets surface-finish lookup values.</summary>
    public Task<IReadOnlyList<OrderLookupItem>> GetSurfaceFinishesAsync(CancellationToken cancellationToken) =>
        GetListAsync<OrderLookupItem>("/materials/SurfaceFinishes", cancellationToken);

    /// <summary>Gets colors supported by one material.</summary>
    public Task<IReadOnlyList<OrderLookupItem>> GetMaterialColorsAsync(int materialId, CancellationToken cancellationToken) =>
        GetListAsync<OrderLookupItem>($"/materials/{materialId}/colors", cancellationToken);

    /// <summary>Gets surface finishes supported by one material.</summary>
    public Task<IReadOnlyList<OrderLookupItem>> GetMaterialSurfaceFinishesAsync(int materialId, CancellationToken cancellationToken) =>
        GetListAsync<OrderLookupItem>($"/materials/{materialId}/surfacefinishes", cancellationToken);

    /// <summary>Gets currency lookup values.</summary>
    public Task<IReadOnlyList<OrderCurrencyItem>> GetCurrenciesAsync(CancellationToken cancellationToken) =>
        GetListAsync<OrderCurrencyItem>("/Currencies", cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken) ?? [];
    }

    private sealed record OrderMaterialPage(IReadOnlyList<OrderMaterialItem> Items);
    private sealed record OrderMaterialItem(int Id, string Name);
}

/// <summary>Reads EmployeeService lookup values needed only by the order editor.</summary>
public sealed class OrderEmployeeReferenceProxy(HttpClient httpClient)
{
    /// <summary>Gets a bounded employee lookup page.</summary>
    public async Task<IReadOnlyList<OrderLookupItem>> GetEmployeesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/employees?sort=EmployeeId_Ascending&search=&index=1&size=250", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<OrderEmployeePage>(cancellationToken);
        return page?.Items.Select(item => new OrderLookupItem(item.Id, item.FullName)).ToArray() ?? [];
    }

    private sealed record OrderEmployeePage(IReadOnlyList<OrderEmployeeItem> Items);
    private sealed record OrderEmployeeItem(int Id, string FullName);
}
