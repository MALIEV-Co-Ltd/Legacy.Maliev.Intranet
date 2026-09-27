using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

internal static class CustomerUpdateEndpointMapper
{
    public static async Task<IResult> UpdateAsync(
        int id,
        CustomerUpdateRequest input,
        CustomersProxy customers,
        CustomerUpdateProxy updates,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return Results.BadRequest();
        }

        var invalidInput = ValidateInput(input);
        if (invalidInput is not null) return invalidInput;

        try
        {
            using var currentResponse = await customers.GetByIdAsync(id, cancellationToken);
            var currentFailure = MapReadFailure(currentResponse, context);
            if (currentFailure is not null)
            {
                return currentFailure;
            }

            var current = await currentResponse.Content.ReadFromJsonAsync<CustomerDetail>(cancellationToken);
            if (!ValidCustomer(current, id)) return InvalidResponse();

            using var updateResponse = await updates.UpdateAsync(id, input, current!, cancellationToken);
            return MapWrite(updateResponse, context);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    internal static bool ValidCustomer(CustomerDetail? current, int id) =>
        current is not null && current.Id == id &&
        !string.IsNullOrWhiteSpace(current.FirstName) &&
        !string.IsNullOrWhiteSpace(current.LastName) &&
        !string.IsNullOrWhiteSpace(current.FullName) &&
        !string.IsNullOrWhiteSpace(current.Email);

    internal static IResult? ValidateInput(CustomerUpdateRequest input)
    {
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
        return null;
    }

    internal static IResult? MapReadFailure(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            CopyRetryAfter(response, context);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return response.IsSuccessStatusCode ? null : Unavailable();
    }

    internal static IResult MapWrite(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest) return Results.BadRequest();
        if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Results.StatusCode((int)response.StatusCode);
        if (response.StatusCode == HttpStatusCode.Conflict) return Results.Conflict();
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        if ((int)response.StatusCode == StatusCodes.Status428PreconditionRequired) return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            CopyRetryAfter(response, context);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return response.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
    }

    private static void CopyRetryAfter(HttpResponseMessage response, HttpContext context)
    {
        if (response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero && retry <= TimeSpan.FromHours(1))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    internal static IResult InvalidResponse() => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid CustomerService response");
    internal static IResult Unavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "CustomerService unavailable");
}
