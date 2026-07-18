using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Bff.Catalog;
using Legacy.Maliev.Intranet.Bff.Customers;
using Legacy.Maliev.Intranet.Bff.Employees;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Server.Quotations;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

internal static class QuotationCreateEndpointMapper
{
    public static async Task<IResult> GetAsync(
        int? customerId,
        HttpContext context,
        EmployeesProxy employees,
        CatalogMaterialsProxy catalog,
        CustomersProxy customers,
        OrdersProxy orders,
        CancellationToken cancellationToken)
    {
        try
        {
            using var employeesResponse = await employees.GetAsync(EmployeeListSort.EmployeeId_Ascending, null, 1, 250, cancellationToken);
            using var currenciesResponse = await catalog.GetCurrenciesAsync(cancellationToken);
            var failure = MapFailure(employeesResponse) ?? MapFailure(currenciesResponse);
            if (failure is not null) return failure;
            var employeePage = await employeesResponse.Content.ReadFromJsonAsync<EmployeeListPage>(cancellationToken);
            var currencies = await currenciesResponse.Content.ReadFromJsonAsync<IReadOnlyList<CurrencySource>>(cancellationToken);
            if (employeePage?.Items is null || currencies is null) return InvalidResponse();

            QuotationCustomer? customer = null;
            IReadOnlyList<OrderListItem> customerOrders = [];
            if (customerId is > 0)
            {
                using var customerResponse = await customers.GetByIdAsync(customerId.Value, cancellationToken);
                if (customerResponse.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
                if ((failure = MapFailure(customerResponse)) is not null) return failure;
                var detail = await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
                if (detail is null || detail.Id != customerId) return InvalidResponse();
                customer = new(detail.Id, detail.FullName, detail.Email, detail.Telephone, detail.Mobile, detail.Fax);

                using var ordersResponse = await orders.GetCustomerAsync(customerId.Value, null, 1, 250, cancellationToken);
                if (ordersResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    if ((failure = MapFailure(ordersResponse)) is not null) return failure;
                    var page = await ordersResponse.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken);
                    if (page?.Items is null || page.Items.Any(item => item.CustomerId != customerId)) return InvalidResponse();
                    customerOrders = page.Items;
                }
            }

            var currentEmployeeId = int.TryParse(
                context.User.FindFirstValue(EmployeeSessionService.LegacyDatabaseIdClaim),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedEmployeeId) && parsedEmployeeId > 0
                    ? (int?)parsedEmployeeId
                    : null;
            return Results.Ok(new QuotationCreatePage(
                employeePage.Items.Select(value => new QuotationEmployee(value.Id, value.FullName, value.Email)).ToArray(),
                currencies.Select(value => new QuotationCurrency(value.Id, value.ShortName, value.LongName ?? value.ShortName)).ToArray(),
                customer,
                customerOrders,
                currentEmployeeId));
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            return Unavailable();
        }
        catch (System.Text.Json.JsonException)
        {
            return InvalidResponse();
        }
    }

    public static async Task<IResult> PostAsync(
        QuotationCreateRequest input,
        HttpContext context,
        QuotationCreationWorkflow workflow,
        IQuotationCreationGateway gateway,
        CustomersProxy customers,
        EmployeesProxy employees,
        CatalogMaterialsProxy catalog,
        OrdersProxy orders,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var header) ||
            !Guid.TryParse(header.ToString(), out var workflowId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["A GUID Idempotency-Key header is required."],
            });
        }

        try
        {
            var referenceFailure = await ValidateReferencesAsync(input, customers, employees, catalog, orders, cancellationToken);
            if (referenceFailure is not null) return referenceFailure;
            var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
            var priced = QuotationPricing.Calculate(input, clock.GetUtcNow());
            var fingerprint = QuotationCreationWorkflow.CreateFingerprint(input);
            var result = await workflow.CreateAsync(
                $"employee:{subject}:quotation-create:{workflowId:D}",
                fingerprint,
                input,
                priced,
                gateway,
                cancellationToken);
            return Results.Created($"/Quotations/View?id={result.Id}", result);
        }
        catch (QuotationCreationConflictException)
        {
            return Results.Conflict(new { error = "This quotation attempt was already used with different input." });
        }
        catch (QuotationCreationBusyException)
        {
            return Results.Conflict(new { error = "This quotation attempt is already being processed." });
        }
        catch (QuotationCreationOutcomeUnknownException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    private static async Task<IResult?> ValidateReferencesAsync(
        QuotationCreateRequest input,
        CustomersProxy customers,
        EmployeesProxy employees,
        CatalogMaterialsProxy catalog,
        OrdersProxy orders,
        CancellationToken cancellationToken)
    {
        using var customerResponse = await customers.GetByIdAsync(input.CustomerId, cancellationToken);
        using var employeeResponse = await employees.GetByIdAsync(input.EmployeeId, cancellationToken);
        using var currenciesResponse = await catalog.GetCurrenciesAsync(cancellationToken);
        if (customerResponse.StatusCode == HttpStatusCode.NotFound || employeeResponse.StatusCode == HttpStatusCode.NotFound)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["References"] = ["The selected customer or employee no longer exists."] });
        var failure = MapFailure(customerResponse) ?? MapFailure(employeeResponse) ?? MapFailure(currenciesResponse);
        if (failure is not null) return failure;
        var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeDetail>(cancellationToken);
        var currencies = await currenciesResponse.Content.ReadFromJsonAsync<IReadOnlyList<CurrencySource>>(cancellationToken);
        if (customer?.Id != input.CustomerId || employee?.Id != input.EmployeeId || currencies is null || currencies.All(value => value.Id != input.CurrencyId))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["References"] = ["One or more selected references are invalid."] });

        var orderIds = input.Lines.Where(line => line.OrderId is > 0).Select(line => line.OrderId!.Value).Distinct().ToHashSet();
        if (orderIds.Count == 0) return null;
        using var ordersResponse = await orders.GetCustomerAsync(input.CustomerId, null, 1, 250, cancellationToken);
        if ((failure = MapFailure(ordersResponse)) is not null) return failure;
        var page = await ordersResponse.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken);
        if (page?.Items is null || orderIds.Except(page.Items.Where(item => item.CustomerId == input.CustomerId).Select(item => item.Id)).Any())
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(QuotationCreateRequest.Lines)] = ["Every linked order must belong to the selected customer."] });
        return null;
    }

    private static Dictionary<string, string[]> Validate(QuotationCreateRequest input)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        ValidateObject(input, string.Empty, errors);
        if (input.Lines is not null)
        {
            for (var index = 0; index < input.Lines.Count; index++) ValidateObject(input.Lines[index], $"Lines[{index}].", errors);
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateObject(object value, string prefix, Dictionary<string, List<string>> errors)
    {
        var failures = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), failures, validateAllProperties: true);
        foreach (var failure in failures)
        {
            foreach (var member in failure.MemberNames.DefaultIfEmpty(string.Empty))
            {
                var key = prefix + member;
                if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
                messages.Add(failure.ErrorMessage ?? "The value is invalid.");
            }
        }
    }

    private static IResult? MapFailure(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => Results.Unauthorized(),
        HttpStatusCode.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ when (int)response.StatusCode >= 500 => Unavailable(),
        _ when !response.IsSuccessStatusCode => Results.StatusCode(StatusCodes.Status502BadGateway),
        _ => null,
    };

    private static bool IsUnavailable(Exception exception, CancellationToken requestToken) =>
        exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException ||
        exception is OperationCanceledException && !requestToken.IsCancellationRequested;

    private static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "A required legacy service is temporarily unavailable.");

    private static IResult InvalidResponse() => Results.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "A required legacy service returned an invalid response.");

    private sealed record CurrencySource(int Id, string ShortName, string? LongName);
}
