using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

internal static class CustomerInternalRemarkEndpointMapper
{
    public static async Task<IResult> GetAsync(
        int id,
        CustomerUpdateProxy remarks,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();

        try
        {
            using var response = await remarks.GetInternalRemarkAsync(id, cancellationToken);
            var failure = MapFailure(response, context, preserveBadRequest: false);
            if (failure is not null) return failure;

            var remark = await response.Content.ReadFromJsonAsync<CustomerInternalRemarkResponse>(cancellationToken);
            return remark is null || remark.CustomerId != id
                ? InvalidResponse()
                : Results.Ok(remark);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> UpdateAsync(
        int id,
        CustomerInternalRemarkUpdateRequest input,
        CustomerUpdateProxy remarks,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), validationResults, true))
        {
            var errors = validationResults
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                    .Select(member => new
                    {
                        member = string.IsNullOrEmpty(member)
                            ? member
                            : System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(member),
                        message = result.ErrorMessage ?? "The value is invalid.",
                    }))
                .GroupBy(error => error.member, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            return Results.ValidationProblem(errors);
        }

        try
        {
            using var response = await remarks.UpdateInternalRemarkAsync(id, input, cancellationToken);
            var failure = MapFailure(response, context, preserveBadRequest: true);
            return failure ?? (response.IsSuccessStatusCode ? Results.NoContent() : Unavailable());
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    private static IResult? MapFailure(HttpResponseMessage response, HttpContext context, bool preserveBadRequest)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict ||
            preserveBadRequest && response.StatusCode == HttpStatusCode.BadRequest)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero && retry <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return response.IsSuccessStatusCode ? null : Unavailable();
    }

    private static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static IResult InvalidResponse() => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response");
    private static IResult Unavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
}
