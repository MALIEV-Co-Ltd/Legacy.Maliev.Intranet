using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Quotations;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Transport-only adapter for the quotation creation workflow.</summary>
public sealed class QuotationCreationGateway(
    IHttpClientFactory clients,
    TimeProvider? timeProvider = null) : IQuotationCreationGateway
{
    /// <summary>Named QuotationService client.</summary>
    public const string QuotationClient = "quotation-create-quotation";
    /// <summary>Named OrderService client.</summary>
    public const string OrderClient = "quotation-create-order";
    /// <summary>Named CustomerService client.</summary>
    public const string CustomerClient = "quotation-create-customer";
    /// <summary>Named EmployeeService client.</summary>
    public const string EmployeeClient = "quotation-create-employee";
    /// <summary>Named CatalogService client.</summary>
    public const string CatalogClient = "quotation-create-catalog";
    /// <summary>Named DocumentService client.</summary>
    public const string DocumentClient = "quotation-create-document";
    /// <summary>Named FileService client.</summary>
    public const string FileClient = "quotation-create-file";
    /// <summary>Named NotificationService client.</summary>
    public const string NotificationClient = "quotation-create-notification";

    private static readonly JsonSerializerOptions TransportJson = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task<int> CreateQuotationAsync(
        QuotationCreateRequest input,
        PricedQuotation priced,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var payload = new QuotationWrite(
            input.CustomerId,
            input.EmployeeId,
            null,
            input.Period,
            now.AddDays(input.Period).UtcDateTime,
            priced.Subtotal,
            priced.Vat,
            priced.Total,
            priced.WithholdingTax,
            priced.QuotedAmount,
            input.CurrencyId,
            input.Comment,
            input.Fob,
            input.ShippedVia,
            input.Terms,
            null,
            now.UtcDateTime,
            now.UtcDateTime);
        return PostForIdAsync(QuotationClient, "/quotations", payload, idempotencyKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CreateLineAsync(
        int quotationId,
        PricedQuotationLine line,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostForIdAsync(
            QuotationClient,
            "/quotations/orderitems",
            new QuotationLineWrite(quotationId, line.OrderId, line.Description, line.Quantity, line.UnitPrice, line.Subtotal),
            idempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CreateOrderLinkAsync(
        int quotationId,
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        PostForIdAsync<object>(
            QuotationClient,
            $"/quotations/{quotationId}/orders/{orderId}",
            content: null,
            idempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public async Task MarkOrderQuotedAsync(int orderId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/orderstatuses/histories/{orderId}/quoted");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await clients.CreateClient(OrderClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task FinalizeDocumentDeliveryAsync(
        int quotationId,
        QuotationCreateRequest input,
        PricedQuotation priced,
        CancellationToken cancellationToken)
    {
        var customerTask = GetAsync<CustomerDetail>(CustomerClient, $"/customers/{input.CustomerId}", cancellationToken);
        var employeeTask = GetAsync<EmployeeDetail>(EmployeeClient, $"/employees/{input.EmployeeId}", cancellationToken);
        var currenciesTask = GetAsync<IReadOnlyList<CurrencySource>>(CatalogClient, "/Currencies", cancellationToken);
        var countriesTask = GetAsync<IReadOnlyList<CountrySource>>(CatalogClient, "/Countries", cancellationToken);
        await Task.WhenAll(customerTask, employeeTask, currenciesTask, countriesTask);

        var customer = customerTask.Result;
        var employee = employeeTask.Result;
        var currency = currenciesTask.Result.Single(value => value.Id == input.CurrencyId);
        var country = customer.BillingAddress is null
            ? string.Empty
            : countriesTask.Result.SingleOrDefault(value => value.Id == customer.BillingAddress.CountryId)?.Name ?? string.Empty;
        var now = clock.GetUtcNow();
        var fileName = $"Quotation_{quotationId}_{now:ddMMyyyy}.pdf";
        var pdfDocument = BuildDocument(quotationId, input, priced, customer, employee, currency.ShortName, country, now);

        var pdf = await RenderPdfAsync(pdfDocument, cancellationToken);
        var stored = await UploadPdfAsync(pdf, fileName, now, cancellationToken);
        await LinkFileAsync(quotationId, stored, cancellationToken);
        await SendEmailAsync(quotationId, customer, pdf, fileName, cancellationToken);
    }

    private async Task<int> PostForIdAsync<T>(
        string clientName,
        string uri,
        T? content,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = content is null ? null : JsonContent.Create(content, options: TransportJson),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await clients.CreateClient(clientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreatedId>(TransportJson, cancellationToken)
            ?? throw new InvalidDataException("The downstream create response did not contain an identifier.");
        return result.Id;
    }

    private async Task<T> GetAsync<T>(string clientName, string uri, CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(clientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(TransportJson, cancellationToken)
            ?? throw new InvalidDataException("The downstream response body was empty.");
    }

    private async Task<byte[]> RenderPdfAsync(QuotationPdf document, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/pdfs/quotation")
        {
            Content = JsonContent.Create(document, options: TransportJson),
        };
        using var response = await clients.CreateClient(DocumentClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/pdf")
        {
            throw new InvalidDataException("DocumentService returned a non-PDF quotation document.");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<StoredObject> UploadPdfAsync(
        byte[] pdf,
        string fileName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "files", fileName);
        var path = $"quotations/{now:yyyy/MM/dd}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(path)}")
        {
            Content = multipart,
        };
        using var response = await clients.CreateClient(FileClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var upload = await response.Content.ReadFromJsonAsync<UploadResult>(TransportJson, cancellationToken)
            ?? throw new InvalidDataException("FileService returned an empty upload response.");
        return upload.Object.SingleOrDefault()
            ?? throw new InvalidDataException("FileService did not return the stored quotation object.");
    }

    private async Task LinkFileAsync(int quotationId, StoredObject stored, CancellationToken cancellationToken)
    {
        var uri = $"/quotations/{quotationId}/files?bucket={Uri.EscapeDataString(stored.Bucket)}&objectName={Uri.EscapeDataString(stored.ObjectName)}";
        using var response = await clients.CreateClient(QuotationClient).PostAsync(uri, content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendEmailAsync(
        int quotationId,
        CustomerDetail customer,
        byte[] pdf,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var multipart = new MultipartFormDataContent();
        var attachment = new ByteArrayContent(pdf);
        attachment.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(attachment, "files", fileName);
        var uri = "/Emails/manufacturing" +
                  $"?to={Uri.EscapeDataString(customer.Email)}" +
                  "&bcc=mail-tracking%40maliev.com" +
                  $"&subject={Uri.EscapeDataString($"Quotation #{quotationId}")}" +
                  $"&body={Uri.EscapeDataString(BuildEmailBody(customer.FullName))}";
        using var response = await clients.CreateClient(NotificationClient).PostAsync(uri, multipart, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static QuotationPdf BuildDocument(
        int quotationId,
        QuotationCreateRequest input,
        PricedQuotation priced,
        CustomerDetail customer,
        EmployeeDetail employee,
        string currency,
        string billingCountry,
        DateTimeOffset now) =>
        new(
            input.Comment,
            now.UtcDateTime,
            currency,
            new(
                customer.Id,
                customer.Company?.Registrar,
                customer.Company?.Name,
                customer.Email,
                customer.Fax,
                customer.FullName,
                customer.Mobile,
                customer.Company?.TaxNumber,
                customer.Telephone,
                customer.BillingAddress?.Building,
                customer.BillingAddress?.AddressLine1,
                customer.BillingAddress?.AddressLine2,
                customer.BillingAddress?.City,
                customer.BillingAddress?.State,
                customer.BillingAddress?.PostalCode,
                billingCountry),
            new(employee.Email, null, employee.FullName, employee.PhoneNumber, employee.PhoneNumber),
            now.AddDays(input.Period).UtcDateTime,
            input.Fob,
            quotationId,
            null,
            priced.Lines.Select((line, index) => new QuotationPdfLine(
                line.OrderId ?? index + 1,
                line.Description,
                input.Lines[index].DiscountPercent,
                null,
                line.OrderId?.ToString() ?? string.Empty,
                line.Quantity,
                line.Subtotal,
                line.UnitPrice)).ToArray(),
            input.Period,
            priced.QuotedAmount,
            input.ShippedVia,
            priced.Subtotal,
            input.Terms,
            priced.Total,
            priced.Vat,
            priced.WithholdingTax);

    private static string BuildEmailBody(string fullName)
    {
        var customer = WebUtility.HtmlEncode(fullName);
        return $"<div>Hello {customer},</div><div>&nbsp;</div>" +
               "<div>Thank you for placing an order with us.</div><div>&nbsp;</div>" +
               "<div>We have reviewed your orders and prepared a quotation for you to review.</div>" +
               "<div>The production of your orders will begin as soon as the payable amount is received.</div>" +
               "<div>&nbsp;</div><div>If you agree with the quoted amount, please let us know or accept the quotation in your account.</div>" +
               "<div>&nbsp;</div><div>Thank you and we look forward to hearing from you.</div>" +
               "<div>&nbsp;</div><div>Best regards,<br>Maliev Co., Ltd.</div>";
    }

    private sealed record CreatedId(int Id);
    private sealed record CurrencySource(int Id, string ShortName, string LongName);
    private sealed record CountrySource(int Id, string Name);
    private sealed record UploadResult(IReadOnlyList<StoredObject> Object);
    private sealed record StoredObject(string Bucket, string ObjectName);
    private sealed record QuotationWrite(
        int CustomerId,
        int EmployeeId,
        int? InvoiceId,
        int Period,
        DateTime ExpirationDate,
        decimal Subtotal,
        decimal Vat,
        decimal Total,
        decimal WithholdingTax,
        decimal QuotedAmount,
        int CurrencyId,
        string? Comment,
        string? Fob,
        string? ShippedVia,
        string? Terms,
        bool? Accepted,
        DateTime CreatedDate,
        DateTime ModifiedDate);
    private sealed record QuotationLineWrite(
        int QuotationId,
        int? OrderId,
        string Description,
        int Quantity,
        decimal UnitPrice,
        decimal Subtotal);
    private sealed record QuotationPdf(
        string? Comment,
        DateTime CreatedDate,
        string Currency,
        QuotationPdfCustomer Customer,
        QuotationPdfEmployee Employee,
        DateTime ExpirationDate,
        string? Fob,
        int Id,
        string? InvoiceNumber,
        IReadOnlyList<QuotationPdfLine> Orders,
        int Period,
        decimal QuotedAmount,
        string? ShippedVia,
        decimal Subtotal,
        string? Terms,
        decimal Total,
        decimal Vat,
        decimal WithholdingTax);
    private sealed record QuotationPdfCustomer(
        int Id,
        string? CommercialRegistrar,
        string? CompanyName,
        string Email,
        string? Fax,
        string FullName,
        string? Mobile,
        string? TaxNumber,
        string? Telephone,
        string? BillingAddressBuilding,
        string? BillingAddressLine1,
        string? BillingAddressLine2,
        string? BillingAddressCity,
        string? BillingAddressState,
        string? BillingAddressPostalCode,
        string BillingAddressCountry);
    private sealed record QuotationPdfEmployee(string Email, string? Fax, string FullName, string? Mobile, string? Telephone);
    private sealed record QuotationPdfLine(
        int Id,
        string Description,
        decimal? Discount,
        int? LeadTime,
        string Name,
        int Quantity,
        decimal Subtotal,
        decimal UnitPrice);
}
