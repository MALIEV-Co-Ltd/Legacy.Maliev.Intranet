using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Intranet.Bff.Employees;

/// <summary>Normalizes the anonymous browser recovery contract without exposing identity existence.</summary>
public static class EmployeeRecoveryEndpointMapper
{
    private static readonly object AcceptedResponse = new
    {
        accepted = true,
        message = "If the employee account exists, recovery instructions will be sent.",
    };

    /// <summary>Requests a password-reset challenge and delivers it without exposing the token to the browser.</summary>
    public static async Task<IResult> RequestPasswordResetAsync(
        EmployeeRecoveryEmailRequest request,
        HttpContext context,
        EmployeeRecoveryAuthProxy auth,
        EmployeeRecoveryNotificationProxy notifications,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (ValidationProblem(request) is { } validation)
        {
            return validation;
        }

        try
        {
            using var response = await auth.RequestPasswordResetAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return DownstreamFailure(response, context);
            }

            var challenge = await response.Content.ReadFromJsonAsync<EmployeeRecoveryChallenge>(cancellationToken);
            if (challenge?.Accepted != true)
            {
                return ServiceUnavailable();
            }

            if (!string.IsNullOrWhiteSpace(challenge.Token))
            {
                var callback = BuildCallbackUrl(context.Request, request.Email, challenge.Token);
                try
                {
                    using var notification = await notifications.SendPasswordResetAsync(
                        request.Email.Trim(), callback, cancellationToken);
                    if (!notification.IsSuccessStatusCode)
                    {
                        loggerFactory.CreateLogger("EmployeeRecovery")
                            .LogWarning(
                                "Employee password recovery notification failed with HTTP {StatusCode}",
                                (int)notification.StatusCode);
                    }
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException)
                {
                    loggerFactory.CreateLogger("EmployeeRecovery")
                        .LogWarning("Employee password recovery notification was unavailable");
                }
            }

            return Results.Accepted(value: AcceptedResponse);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return ServiceUnavailable();
        }
    }

    /// <summary>Completes an employee password reset through AuthService.</summary>
    public static Task<IResult> CompletePasswordResetAsync(
        EmployeePasswordResetRequest request,
        HttpContext context,
        EmployeeRecoveryAuthProxy auth,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            request,
            context,
            token => auth.CompletePasswordResetAsync(request, token),
            cancellationToken);

    /// <summary>Completes an employee email confirmation through AuthService.</summary>
    public static Task<IResult> CompleteEmailConfirmationAsync(
        EmployeeEmailConfirmationRequest request,
        HttpContext context,
        EmployeeRecoveryAuthProxy auth,
        CancellationToken cancellationToken) =>
        CompleteAsync(
            request,
            context,
            token => auth.CompleteEmailConfirmationAsync(request, token),
            cancellationToken);

    private static async Task<IResult> CompleteAsync<TRequest>(
        TRequest request,
        HttpContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        if (ValidationProblem(request) is { } validation)
        {
            return validation;
        }

        try
        {
            using var response = await send(cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => Results.NoContent(),
                HttpStatusCode.BadRequest => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Identity action failed",
                    detail: "The identity action is invalid or expired."),
                _ => DownstreamFailure(response, context),
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return ServiceUnavailable();
        }
    }

    private static IResult? ValidationProblem<TRequest>(TRequest request)
        where TRequest : class
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, true))
        {
            return null;
        }

        var errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new
                {
                    member,
                    message = result.ErrorMessage ?? "The value is invalid.",
                }))
            .GroupBy(error => error.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        return Results.ValidationProblem(errors);
    }

    private static IResult DownstreamFailure(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter.HasValue
                && retryAfter.Value > TimeSpan.Zero
                && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.Value.TotalSeconds)
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Employee recovery throttled",
                detail: "Too many recovery attempts. Wait and try again.");
        }

        return ServiceUnavailable();
    }

    private static IResult ServiceUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Employee recovery unavailable",
        detail: "Employee recovery is temporarily unavailable.");

    private static string BuildCallbackUrl(HttpRequest request, string email, string token)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/Employees/ResetPassword";
        return QueryHelpers.AddQueryString(baseUrl, new Dictionary<string, string?>
        {
            ["email"] = email.Trim(),
            ["token"] = token,
        });
    }
}
