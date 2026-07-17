using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Legacy.Maliev.Intranet.Bff.Customers;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;

namespace Legacy.Maliev.Intranet.Bff.Orders;

internal static class OrderCreateEndpointMapper
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> GetAsync(
        int? customerId,
        OrdersProxy orders,
        OrderCatalogReferenceProxy catalog,
        CustomersProxy customers,
        CancellationToken cancellationToken)
    {
        if (customerId is <= 0) return Results.BadRequest();
        try
        {
            using var processesResponse = await orders.GetProcessesAsync(cancellationToken);
            processesResponse.EnsureSuccessStatusCode();
            var processes = await processesResponse.Content.ReadFromJsonAsync<List<OrderProcessItem>>(cancellationToken) ?? [];
            var materials = await catalog.GetMaterialsAsync(cancellationToken);
            CustomerDetail? customer = null;
            if (customerId is not null)
            {
                using var customerResponse = await customers.GetByIdAsync(customerId.Value, cancellationToken);
                if (customerResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    customerResponse.EnsureSuccessStatusCode();
                    customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
                }
            }

            return Results.Ok(new OrderCreatePage(
                processes.Select(item => new OrderLookupItem(item.Id, item.Name)).ToArray(),
                materials,
                customer));
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> GetMaterialOptionsAsync(
        int materialId,
        OrderCatalogReferenceProxy catalog,
        CancellationToken cancellationToken)
    {
        if (materialId <= 0) return Results.BadRequest();
        try
        {
            var colors = catalog.GetMaterialColorsAsync(materialId, cancellationToken);
            var finishes = catalog.GetMaterialSurfaceFinishesAsync(materialId, cancellationToken);
            await Task.WhenAll(colors, finishes);
            return Results.Ok(new OrderMaterialOptions(await colors, await finishes));
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> CreateAsync(
        HttpRequest request,
        HttpContext context,
        CustomersProxy customers,
        OrdersProxy orderReferences,
        OrderCatalogReferenceProxy catalog,
        OrderCreateProxy createOrders,
        OrderDetailProxy orderFiles,
        OrderFileProxy files,
        OrderNotificationProxy notifications,
        OrderCreationWorkflow workflow,
        ILogger<OrderCreationWorkflow> logger,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType ||
            !Guid.TryParse(request.Headers["Idempotency-Key"].FirstOrDefault(), out var workflowId))
        {
            return Results.BadRequest();
        }

        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var input = JsonSerializer.Deserialize<OrderCreateRequest>(form["request"].FirstOrDefault() ?? string.Empty, WebJson);
            var failures = new List<ValidationResult>();
            if (input is null || !Validator.TryValidateObject(input, new ValidationContext(input), failures, true))
            {
                return Results.ValidationProblem(ToValidationErrors(failures));
            }

            var uploads = form.Files.Where(file => file.Length > 0).ToArray();
            if (uploads.Sum(file => file.Length) > 200L * 1024 * 1024) return Results.BadRequest();
            var referenceFailure = await ValidateReferencesAsync(input, orderReferences, catalog, cancellationToken);
            if (referenceFailure is not null) return referenceFailure;

            using var customerResponse = await customers.GetByIdAsync(input.CustomerId, cancellationToken);
            if (customerResponse.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            customerResponse.EnsureSuccessStatusCode();
            var customer = await customerResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
            if (customer is null) return Results.NotFound();
            if (input.SendConfirmationEmail && string.IsNullOrWhiteSpace(customer.Email))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(CustomerDetail.Email)] = ["The selected customer does not have an email address."],
                });
            }

            var employeeId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(employeeId)) return Results.Unauthorized();
            var fingerprint = await OrderCreationWorkflow.CreateFingerprintAsync(input, uploads, cancellationToken);
            var workflowKey = $"employee:{employeeId}:order-create:{workflowId:D}";

            var result = await workflow.CreateAsync(
                workflowKey,
                fingerprint,
                input,
                customer.Email,
                uploads,
                async (payload, idempotencyKey, token) =>
                {
                    try
                    {
                        using var response = await createOrders.CreateAsync(payload, idempotencyKey, token);
                        response.EnsureSuccessStatusCode();
                        return (await response.Content.ReadFromJsonAsync<CreatedOrder>(token)
                            ?? throw new InvalidDataException("OrderService returned an empty order.")).Id;
                    }
                    catch (HttpRequestException exception) when (IsAmbiguousServerFailure(exception))
                    {
                        throw new OrderCreationOutcomeUnknownException(
                            "The OrderService create outcome will be replayed with the same downstream attempt key.",
                            exception);
                    }
                    catch (Exception exception) when (IsCancellationOrTimeout(exception))
                    {
                        throw new OrderCreationOutcomeUnknownException(
                            "The OrderService create outcome will be replayed with the same downstream attempt key.",
                            exception);
                    }
                },
                async (customerId, selectedFiles, idempotencyKey, token) =>
                {
                    try
                    {
                        using var response = await files.UploadAsync(customerId, selectedFiles, idempotencyKey, token);
                        if (response.StatusCode == HttpStatusCode.Conflict)
                        {
                            var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(token);
                            if (string.Equals(problem?.Title, "Upload replay conflict", StringComparison.Ordinal))
                            {
                                throw new OrderCreationConflictException();
                            }
                            if (string.Equals(problem?.Title, "Upload in progress", StringComparison.Ordinal))
                            {
                                throw new OrderCreationBusyException();
                            }
                            throw new OrderCreationOutcomeUnknownException("The FileService replay state was not recognized.");
                        }
                        if ((int)response.StatusCode >= StatusCodes.Status500InternalServerError)
                        {
                            throw new OrderCreationOutcomeUnknownException(
                                "The FileService upload outcome will be replayed with the same downstream attempt key.");
                        }
                        response.EnsureSuccessStatusCode();
                        var uploaded = await response.Content.ReadFromJsonAsync<OrderUploadResult>(token)
                            ?? throw new InvalidDataException("FileService returned an empty upload result.");
                        return uploaded.Object.Select(item => new StoredOrderFile(0, 0, item.Bucket, item.ObjectName, item.Uri)).ToArray();
                    }
                    catch (HttpRequestException exception) when (IsAmbiguousServerFailure(exception))
                    {
                        throw new OrderCreationOutcomeUnknownException(
                            "The FileService upload outcome will be replayed with the same downstream attempt key.",
                            exception);
                    }
                    catch (Exception exception) when (IsCancellationOrTimeout(exception))
                    {
                        throw new OrderCreationOutcomeUnknownException(
                            "The FileService upload outcome will be replayed with the same downstream attempt key.",
                            exception);
                    }
                },
                async (orderId, stored, token) =>
                {
                    return await EnsureOrderFileAsync(orderId, stored, orderFiles, token);
                },
                async (orderId, idempotencyKey, token) =>
                {
                    using var response = await createOrders.CreateInitialStatusAsync(orderId, idempotencyKey, token);
                    response.EnsureSuccessStatusCode();
                },
                async (email, orderId, token) =>
                {
                    using var response = await notifications.SendCreatedAsync(email, orderId, token);
                    response.EnsureSuccessStatusCode();
                },
                async (fileId, token) =>
                {
                    using var response = await orderFiles.DeleteFileAsync(fileId, token);
                    if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                async (stored, token) =>
                {
                    using var response = await files.DeleteAsync(stored.Bucket, stored.ObjectName, token);
                    if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                async (orderId, token) =>
                {
                    using var response = await createOrders.DeleteAsync(orderId, token);
                    if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                cancellationToken);
            return Results.Ok(result);
        }
        catch (OrderCreationConflictException)
        {
            return Results.Conflict();
        }
        catch (OrderCreationBusyException)
        {
            return Results.Conflict();
        }
        catch (OrderCreationOutcomeUnknownException exception)
        {
            logger.LogWarning(exception, "Order creation has an unresolved downstream outcome and was not compensated.");
            return Unavailable();
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order creation failed at a downstream boundary.");
            return Unavailable();
        }
    }

    private static async Task<IResult?> ValidateReferencesAsync(
        OrderCreateRequest input,
        OrdersProxy orders,
        OrderCatalogReferenceProxy catalog,
        CancellationToken cancellationToken)
    {
        using var processesResponse = await orders.GetProcessesAsync(cancellationToken);
        processesResponse.EnsureSuccessStatusCode();
        var processes = await processesResponse.Content.ReadFromJsonAsync<List<OrderProcessItem>>(cancellationToken) ?? [];
        if (processes.All(item => item.Id != input.ProcessId)) return InvalidReference(nameof(input.ProcessId));

        var materials = await catalog.GetMaterialsAsync(cancellationToken);
        if (input.MaterialId is null)
        {
            return input.ColorId is null && input.SurfaceFinishId is null
                ? null
                : InvalidReference(nameof(input.MaterialId));
        }

        if (materials.All(item => item.Id != input.MaterialId.Value)) return InvalidReference(nameof(input.MaterialId));
        var colorsTask = catalog.GetMaterialColorsAsync(input.MaterialId.Value, cancellationToken);
        var finishesTask = catalog.GetMaterialSurfaceFinishesAsync(input.MaterialId.Value, cancellationToken);
        await Task.WhenAll(colorsTask, finishesTask);
        if (input.ColorId is not null && (await colorsTask).All(item => item.Id != input.ColorId.Value))
        {
            return InvalidReference(nameof(input.ColorId));
        }
        if (input.SurfaceFinishId is not null && (await finishesTask).All(item => item.Id != input.SurfaceFinishId.Value))
        {
            return InvalidReference(nameof(input.SurfaceFinishId));
        }
        return null;
    }

    private static IResult InvalidReference(string member) => Results.ValidationProblem(
        new Dictionary<string, string[]> { [member] = ["The selected value is not available."] });

    private static async Task<int> EnsureOrderFileAsync(
        int orderId,
        StoredOrderFile stored,
        OrderDetailProxy orders,
        CancellationToken cancellationToken)
    {
        var existing = await FindOrderFileAsync(orderId, stored, orders, cancellationToken);
        if (existing is not null) return existing.Id;
        try
        {
            using var response = await orders.CreateFileAsync(orderId, stored.Bucket, stored.ObjectName, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<StoredOrderFile>(cancellationToken)
                ?? throw new InvalidDataException("OrderService returned an empty file record.")).Id;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException or Polly.Timeout.TimeoutRejectedException)
        {
            using var reconciliation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                existing = await FindOrderFileAsync(orderId, stored, orders, reconciliation.Token);
                if (existing is not null) return existing.Id;
            }
            catch (Exception reconciliationFailure) when (
                reconciliationFailure is HttpRequestException or OperationCanceledException or Polly.Timeout.TimeoutRejectedException)
            {
                throw new OrderCreationOutcomeUnknownException("The order-file link outcome could not be reconciled.", reconciliationFailure);
            }
            throw new OrderCreationOutcomeUnknownException("The order-file link outcome is not yet visible.", exception);
        }
    }

    private static async Task<StoredOrderFile?> FindOrderFileAsync(
        int orderId,
        StoredOrderFile stored,
        OrderDetailProxy orders,
        CancellationToken cancellationToken)
    {
        using var response = await orders.GetFilesAsync(orderId, cancellationToken);
        response.EnsureSuccessStatusCode();
        var files = await response.Content.ReadFromJsonAsync<List<StoredOrderFile>>(cancellationToken) ?? [];
        return files.FirstOrDefault(item =>
            string.Equals(item.Bucket, stored.Bucket, StringComparison.Ordinal) &&
            string.Equals(item.ObjectName, stored.ObjectName, StringComparison.Ordinal));
    }

    private static Dictionary<string, string[]> ToValidationErrors(IEnumerable<ValidationResult> failures) => failures
        .SelectMany(failure => failure.MemberNames.DefaultIfEmpty(string.Empty)
            .Select(member => (member, failure.ErrorMessage ?? "The value is invalid.")))
        .GroupBy(failure => failure.member, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Select(failure => failure.Item2).ToArray(), StringComparer.Ordinal);

    private static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or JsonException or Polly.Timeout.TimeoutRejectedException or StackExchange.Redis.RedisException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static bool IsCancellationOrTimeout(Exception exception) =>
        exception is OperationCanceledException or Polly.Timeout.TimeoutRejectedException;

    private static bool IsAmbiguousServerFailure(HttpRequestException exception) =>
        exception.StatusCode is null || (int)exception.StatusCode.Value >= StatusCodes.Status500InternalServerError;

    private static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Order creation unavailable");

    private sealed record CreatedOrder(int Id);
}
