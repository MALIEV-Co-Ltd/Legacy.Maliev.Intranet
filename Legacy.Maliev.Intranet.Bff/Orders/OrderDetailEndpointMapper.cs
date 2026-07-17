using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;

namespace Legacy.Maliev.Intranet.Bff.Orders;

internal static class OrderDetailEndpointMapper
{
    public static async Task<IResult> GetAsync(int id, OrderDetailAggregator aggregator, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            var page = await aggregator.GetAsync(id, cancellationToken);
            return page is null ? Results.NotFound() : Results.Ok(page);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> UpdateAsync(
        int id,
        OrderUpdateRequest input,
        OrderDetailProxy orders,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationResult>();
        if (id <= 0 || !Validator.TryValidateObject(input, new ValidationContext(input), failures, true))
        {
            return Results.ValidationProblem(failures
                .SelectMany(failure => failure.MemberNames.DefaultIfEmpty(string.Empty)
                    .Select(member => (member, failure.ErrorMessage ?? "The value is invalid.")))
                .GroupBy(failure => failure.member, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.Item2).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal));
        }

        try
        {
            using var currentResponse = await orders.GetAsync(id, cancellationToken);
            if (currentResponse.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            currentResponse.EnsureSuccessStatusCode();
            var current = await currentResponse.Content.ReadFromJsonAsync<OrderDetailItem>(cancellationToken)
                ?? throw new InvalidDataException("OrderService returned an empty order.");
            using var response = await orders.UpdateAsync(id, current.CustomerId, input, cancellationToken);
            return MapWriteResponse(response, context);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> TransitionAsync(
        int id,
        int statusId,
        string? idempotencyKey,
        OrderDetailProxy orders,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || statusId <= 0 || !Guid.TryParse(idempotencyKey, out _)) return Results.BadRequest();
        try
        {
            using var response = await orders.TransitionStatusAsync(id, statusId, idempotencyKey!, cancellationToken);
            return MapWriteResponse(response, context);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> UploadAsync(
        int id,
        HttpRequest request,
        OrderDetailProxy orders,
        OrderFileProxy files,
        OrderFileWorkflow workflow,
        ILogger<OrderFileWorkflow> logger,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || !request.HasFormContentType) return Results.BadRequest();
        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var uploads = form.Files.Where(file => file.Length > 0).ToArray();
            if (uploads.Length == 0 || uploads.Sum(file => file.Length) > 200L * 1024 * 1024) return Results.BadRequest();
            using var orderResponse = await orders.GetAsync(id, cancellationToken);
            if (orderResponse.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            orderResponse.EnsureSuccessStatusCode();
            var order = await orderResponse.Content.ReadFromJsonAsync<OrderDetailItem>(cancellationToken);
            if (order?.CustomerId is not > 0) return Results.NotFound();

            var linked = await workflow.UploadAsync(
                id,
                order.CustomerId.Value,
                uploads,
                async (customerId, selected, token) =>
                {
                    using var uploadResponse = await files.UploadAsync(customerId, selected, token);
                    uploadResponse.EnsureSuccessStatusCode();
                    var result = await uploadResponse.Content.ReadFromJsonAsync<OrderUploadResult>(token)
                        ?? throw new InvalidDataException("FileService returned an empty upload result.");
                    return result.Object.Select(item => new StoredOrderFile(0, id, item.Bucket, item.ObjectName, item.Uri)).ToArray();
                },
                async (orderId, stored, token) =>
                {
                    using var linkResponse = await orders.CreateFileAsync(orderId, stored.Bucket, stored.ObjectName, token);
                    linkResponse.EnsureSuccessStatusCode();
                    var linkedFile = await linkResponse.Content.ReadFromJsonAsync<StoredOrderFile>(token)
                        ?? throw new InvalidDataException("OrderService returned an empty file record.");
                    return new OrderFileItem(linkedFile.Id, linkedFile.OrderId, linkedFile.ObjectName, stored.Uri);
                },
                async (linkedFile, token) =>
                {
                    using var response = await orders.DeleteFileAsync(linkedFile.Id, token); response.EnsureSuccessStatusCode();
                },
                async (stored, token) =>
                {
                    using var response = await files.DeleteAsync(stored.Bucket, stored.ObjectName, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                cancellationToken);
            return Results.Ok(linked);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order file upload failed for order {OrderId}.", id);
            return Unavailable();
        }
    }

    public static async Task<IResult> RemoveFileAsync(
        int id,
        int fileId,
        OrderDetailProxy orders,
        OrderFileProxy files,
        OrderFileWorkflow workflow,
        ILogger<OrderFileWorkflow> logger,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || fileId <= 0) return Results.BadRequest();
        try
        {
            using var metadataResponse = await orders.GetFilesAsync(id, cancellationToken);
            if (metadataResponse.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            metadataResponse.EnsureSuccessStatusCode();
            var owned = await metadataResponse.Content.ReadFromJsonAsync<List<StoredOrderFile>>(cancellationToken) ?? [];
            var removed = await workflow.RemoveAsync(
                fileId,
                owned,
                async (stored, token) =>
                {
                    using var response = await files.DeleteAsync(stored.Bucket, stored.ObjectName, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                async (ownedId, token) =>
                {
                    using var response = await orders.DeleteFileAsync(ownedId, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                },
                cancellationToken);
            return removed ? Results.NoContent() : Results.NotFound();
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order file removal failed for order {OrderId} and file {FileId}.", id, fileId);
            return Unavailable();
        }
    }

    public static async Task<IResult> LabelAsync(
        int id,
        OrderDetailAggregator aggregator,
        OrderDocumentProxy documents,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await aggregator.GetLabelAsync(id, cancellationToken);
            if (data is null) return Results.NotFound();
            var payload = new OrderLabelPayload(data.Id, data.Name, data.OrderQuantity, data.ManufactureQuantity, data.RemainingQuantity, data.Process, data.Material, data.Color, data.SurfaceFinish, data.Description);
            var pdf = await documents.RenderAsync(payload, cancellationToken);
            return pdf is null
                ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid DocumentService response")
                : Results.File(pdf, "application/pdf", $"OrderLabel_{id}.pdf");
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    private static IResult MapWriteResponse(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
        if (response.StatusCode == HttpStatusCode.Conflict) return Results.Conflict();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Results.StatusCode((int)response.StatusCode);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero && retry <= TimeSpan.FromHours(1))
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        return response.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
    }

    private static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static IResult Unavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Order workflow unavailable");
}
