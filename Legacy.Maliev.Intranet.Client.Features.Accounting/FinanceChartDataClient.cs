using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client.Features.Accounting;

internal sealed record FinanceChartTrendData(
    int? CurrencyId,
    IReadOnlyDictionary<DateTime, decimal> Income,
    IReadOnlyDictionary<DateTime, decimal> Expense);

internal sealed class FinanceChartDataException(HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception("Finance chart data could not be loaded.", innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

internal static class FinanceChartDataClient
{
    public static async Task<FinanceChartTrendData> LoadAsync(
        HttpClient http,
        int? year,
        bool includeExpense,
        CancellationToken cancellationToken = default)
    {
        using var currencyResponse = await http.GetAsync("/bff/catalog/currencies", cancellationToken);
        EnsureSuccess(currencyResponse, cancellationToken);
        var currencies = await ReadAsync<List<CatalogCurrency>>(currencyResponse, cancellationToken);
        var currencyId = currencies.FirstOrDefault(value =>
            string.Equals(value.ShortName, "THB", StringComparison.OrdinalIgnoreCase))?.Id;
        var currencyQuery = currencyId is int id ? $"&currencyId={id}" : string.Empty;
        var yearQuery = year is int requestedYear ? $"year={requestedYear}" : "year=";
        var incomeTask = http.GetAsync(
            $"/bff/finances/trends/yearly-income?{yearQuery}{currencyQuery}",
            cancellationToken);
        var expenseTask = includeExpense
            ? http.GetAsync(
                $"/bff/finances/trends/yearly-expense?{currencyQuery.TrimStart('&')}",
                cancellationToken)
            : null;
        if (expenseTask is not null)
        {
            await Task.WhenAll(incomeTask, expenseTask);
        }

        using var incomeResponse = await incomeTask;
        EnsureSuccess(incomeResponse, cancellationToken);
        var income = await ReadAsync<Dictionary<DateTime, decimal>>(incomeResponse, cancellationToken);
        if (expenseTask is null)
        {
            return new(currencyId, income, new Dictionary<DateTime, decimal>());
        }

        using var expenseResponse = await expenseTask;
        EnsureSuccess(expenseResponse, cancellationToken);
        var expense = await ReadAsync<Dictionary<DateTime, decimal>>(expenseResponse, cancellationToken);
        return new(currencyId, income, expense);
    }

    private static void EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            throw new FinanceChartDataException(response.StatusCode);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new FinanceChartDataException(HttpStatusCode.BadGateway);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new FinanceChartDataException(HttpStatusCode.BadGateway, exception);
        }
    }
}
