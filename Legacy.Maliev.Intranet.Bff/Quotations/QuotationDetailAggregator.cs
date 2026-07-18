using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Bff.Accounting;
using Legacy.Maliev.Intranet.Bff.Catalog;
using Legacy.Maliev.Intranet.Bff.Customers;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Aggregates a read-only quotation projection across existing bounded contexts.</summary>
public sealed class QuotationDetailAggregator(
    QuotationsProxy quotations,
    CustomersProxy customers,
    EmployeesProxy employees,
    CatalogMaterialsProxy catalog,
    InvoicesProxy invoices,
    QuotationFileProxy files)
{
    /// <summary>Gets one complete browser-safe quotation detail, or null when the quotation does not exist.</summary>
    public async Task<QuotationDetailPage?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var quotationResponse = await quotations.GetAsync(id, cancellationToken);
        if (quotationResponse.StatusCode == HttpStatusCode.NotFound) return null;
        var quotation = await ReadAsync<QuotationListItem>(quotationResponse, cancellationToken)
            ?? throw new InvalidDataException("QuotationService returned an empty quotation.");
        if (quotation.Id != id || quotation.CurrencyId < 1) throw new InvalidDataException("QuotationService returned an invalid quotation owner.");

        var customerTask = quotation.CustomerId is int customerId ? customers.GetByIdAsync(customerId, cancellationToken) : null;
        var employeeTask = quotation.EmployeeId is int employeeId ? employees.GetByIdAsync(employeeId, cancellationToken) : null;
        var invoiceTask = quotation.InvoiceId is int invoiceId ? invoices.GetAsync(invoiceId, cancellationToken) : null;
        var currencyTask = catalog.GetCurrenciesAsync(cancellationToken);
        var ordersTask = quotations.GetOrdersAsync(id, cancellationToken);
        var metadataTask = quotations.GetFilesAsync(id, cancellationToken);
        var tasks = new List<Task> { currencyTask, ordersTask, metadataTask };
        if (customerTask is not null) tasks.Add(customerTask);
        if (employeeTask is not null) tasks.Add(employeeTask);
        if (invoiceTask is not null) tasks.Add(invoiceTask);
        await Task.WhenAll(tasks);

        var customer = customerTask is null ? null : await ReadCustomerAsync(await customerTask, quotation.CustomerId!.Value, cancellationToken);
        var employee = employeeTask is null ? null : await ReadEmployeeAsync(await employeeTask, quotation.EmployeeId!.Value, cancellationToken);
        var invoice = invoiceTask is null ? null : await ReadInvoiceAsync(await invoiceTask, quotation.InvoiceId!.Value, cancellationToken);
        using var currencyResponse = await currencyTask;
        var currencies = await ReadAsync<List<CurrencySource>>(currencyResponse, cancellationToken) ?? [];
        var currency = currencies.SingleOrDefault(item => item.Id == quotation.CurrencyId)
            ?? throw new InvalidDataException("CatalogService did not return the quotation currency.");

        using var ordersResponse = await ordersTask;
        var orders = ordersResponse.StatusCode == HttpStatusCode.NotFound ? [] : await ReadAsync<List<QuotationOrderLink>>(ordersResponse, cancellationToken) ?? [];
        if (orders.Any(item => item.Id < 1 || item.QuotationId != id || item.OrderId < 1)) throw new InvalidDataException("QuotationService returned invalid order ownership.");

        using var metadataResponse = await metadataTask;
        var metadata = metadataResponse.StatusCode == HttpStatusCode.NotFound ? [] : await ReadAsync<List<FileSource>>(metadataResponse, cancellationToken) ?? [];
        if (metadata.Any(item => item.Id < 1 || item.QuotationId != id || string.IsNullOrWhiteSpace(item.Bucket) || string.IsNullOrWhiteSpace(item.ObjectName))) throw new InvalidDataException("QuotationService returned invalid file ownership.");
        using var limiter = new SemaphoreSlim(4);
        var resolved = await Task.WhenAll(metadata.Select(item => ResolveAsync(item, limiter, cancellationToken)));
        return new(quotation, customer, employee, new(currency.Id, currency.ShortName, currency.LongName), invoice, orders, resolved);
    }

    private async Task<QuotationFile> ResolveAsync(FileSource item, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        try
        {
            using var response = await files.GetSignedUrlAsync(item.Bucket, item.ObjectName, cancellationToken);
            var uri = response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<Uri>(response, cancellationToken);
            return new(item.Id, item.QuotationId, item.ObjectName, item.CreatedDate, uri);
        }
        finally { limiter.Release(); }
    }

    private static async Task<QuotationCustomer> ReadCustomerAsync(HttpResponseMessage response, int expectedId, CancellationToken token)
    {
        using (response)
        {
            var value = await ReadAsync<CustomerSource>(response, token) ?? throw new InvalidDataException();
            if (value.Id != expectedId) throw new InvalidDataException();
            return new(value.Id, value.FullName, value.Email, value.Telephone, value.Mobile, value.Fax);
        }
    }

    private static async Task<QuotationEmployee> ReadEmployeeAsync(HttpResponseMessage response, int expectedId, CancellationToken token)
    {
        using (response)
        {
            var value = await ReadAsync<EmployeeSource>(response, token) ?? throw new InvalidDataException();
            if (value.Id != expectedId) throw new InvalidDataException();
            return new(value.Id, value.FullName, value.Email);
        }
    }

    private static async Task<QuotationInvoice> ReadInvoiceAsync(HttpResponseMessage response, int expectedId, CancellationToken token)
    {
        using (response)
        {
            var value = await ReadAsync<InvoiceSource>(response, token) ?? throw new InvalidDataException();
            if (value.Id != expectedId) throw new InvalidDataException();
            return new(value.Id, value.Number);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(token);
    }

    private sealed record CustomerSource(int Id, string FullName, string Email, string? Telephone, string? Mobile, string? Fax);
    private sealed record EmployeeSource(int Id, string FullName, string Email);
    private sealed record CurrencySource(int Id, string ShortName, string LongName);
    private sealed record InvoiceSource(int Id, string Number);
    private sealed record FileSource(int Id, int QuotationId, string Bucket, string ObjectName, DateTime? CreatedDate);
}
