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

    /// <summary>Whether a trusted callback origin is available before creating an unconfirmed account.</summary>
    public static bool HasTrustedCallbackOrigin(IConfiguration configuration) =>
        TryGetTrustedOrigin(configuration, out _);

    /// <summary>Requests and delivers a confirmation challenge after an identity is committed.</summary>
    public static async Task<bool> SendEmailConfirmationAsync(
        string email,
        IConfiguration configuration,
        EmployeeRecoveryAuthProxy auth,
        EmployeeRecoveryNotificationProxy notifications,
        ILogger logger,
        CancellationToken cancellationToken) =>
        (await RequestAndDeliverConfirmationAsync(email, configuration, auth, notifications, logger, cancellationToken)).EmailSent;

    private enum ConfirmationRequestStatus { Accepted, SourceUnavailable, SourceThrottled }
    private sealed record ConfirmationRequestResult(ConfirmationRequestStatus Status, bool EmailSent = false, TimeSpan? RetryAfter = null);

    private static async Task<ConfirmationRequestResult> RequestAndDeliverConfirmationAsync(
        string email,
        IConfiguration configuration,
        EmployeeRecoveryAuthProxy auth,
        EmployeeRecoveryNotificationProxy notifications,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetTrustedOrigin(configuration, out var origin))
        {
            logger.LogError("Employee confirmation callback origin is not configured safely");
            return new(ConfirmationRequestStatus.SourceUnavailable);
        }

        try
        {
            using var response = await auth.RequestEmailConfirmationAsync(new(email), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Employee confirmation challenge failed with HTTP {StatusCode}", (int)response.StatusCode);
                return response.StatusCode == HttpStatusCode.TooManyRequests
                    ? new(ConfirmationRequestStatus.SourceThrottled, RetryAfter: BoundedRetryAfter(response))
                    : new(ConfirmationRequestStatus.SourceUnavailable);
            }

            var challenge = await response.Content.ReadFromJsonAsync<EmployeeRecoveryChallenge>(cancellationToken);
            if (challenge?.Accepted != true)
            {
                logger.LogWarning("Employee confirmation challenge was not available");
                return new(ConfirmationRequestStatus.SourceUnavailable);
            }
            if (string.IsNullOrWhiteSpace(challenge.Token)) return new(ConfirmationRequestStatus.Accepted);

            var callback = QueryHelpers.AddQueryString(
                new Uri(origin, "/Employees/EmailConfirmation").ToString(),
                new Dictionary<string, string?> { ["email"] = email.Trim(), ["token"] = challenge.Token });
            try
            {
                using var notification = await notifications.SendEmailConfirmationAsync(email.Trim(), callback, cancellationToken);
                if (notification.IsSuccessStatusCode) return new(ConfirmationRequestStatus.Accepted, EmailSent: true);
                logger.LogWarning("Employee confirmation notification failed with HTTP {StatusCode}", (int)notification.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning("Employee confirmation notification was unavailable");
            }
            return new(ConfirmationRequestStatus.Accepted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Employee confirmation challenge was unavailable");
            return new(ConfirmationRequestStatus.SourceUnavailable);
        }
    }

    /// <summary>Accepts a resend request without revealing whether an employee identity exists.</summary>
    public static async Task<IResult> RequestEmailConfirmationAsync(
        EmployeeRecoveryEmailRequest request,
        HttpContext context,
        IConfiguration configuration,
        EmployeeRecoveryAuthProxy auth,
        EmployeeRecoveryNotificationProxy notifications,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (ValidationProblem(request) is { } validation) return validation;
        if (!TryGetTrustedOrigin(configuration, out _)) return ServiceUnavailable();

        var result = await RequestAndDeliverConfirmationAsync(request.Email, configuration, auth, notifications,
            loggerFactory.CreateLogger("EmployeeRecovery"), cancellationToken);
        return result.Status switch
        {
            ConfirmationRequestStatus.Accepted => Results.Accepted(value: AcceptedResponse),
            ConfirmationRequestStatus.SourceThrottled => Throttled(context, result.RetryAfter),
            _ => ServiceUnavailable(),
        };
    }

    /// <summary>Requests a password-reset challenge and delivers it without exposing the token to the browser.</summary>
    public static async Task<IResult> RequestPasswordResetAsync(
        EmployeeRecoveryEmailRequest request,
        HttpContext context,
        IConfiguration configuration,
        EmployeeRecoveryAuthProxy auth,
        EmployeeRecoveryNotificationProxy notifications,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ValidationProblem(request) is { } validation)
        {
            return validation;
        }
        if (!TryGetTrustedOrigin(configuration, out var origin)) return ServiceUnavailable();

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
                var callback = BuildCallbackUrl(origin, "/Employees/ResetPassword", request.Email, challenge.Token);
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException)
                {
                    loggerFactory.CreateLogger("EmployeeRecovery")
                        .LogWarning("Employee password recovery notification was unavailable");
                }
            }

            return Results.Accepted(value: AcceptedResponse);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
        cancellationToken.ThrowIfCancellationRequested();
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
        return response.StatusCode == HttpStatusCode.TooManyRequests
            ? Throttled(context, BoundedRetryAfter(response)) : ServiceUnavailable();
    }

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromHours(1)
            ? delta : null;

    private static IResult Throttled(HttpContext context, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delta)
            context.Response.Headers.RetryAfter = Math.Ceiling(delta.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
            title: "Employee recovery throttled", detail: "Too many recovery attempts. Wait and try again.");
    }

    private static IResult ServiceUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Employee recovery unavailable",
        detail: "Employee recovery is temporarily unavailable.");

    private static string BuildCallbackUrl(Uri origin, string path, string email, string token)
    {
        var baseUrl = new Uri(origin, path).ToString();
        return QueryHelpers.AddQueryString(baseUrl, new Dictionary<string, string?>
        {
            ["email"] = email.Trim(),
            ["token"] = token,
        });
    }

    private static bool TryGetTrustedOrigin(IConfiguration configuration, out Uri origin)
    {
        var value = configuration["EmployeeConfirmation:PublicOrigin"];
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(parsed.UserInfo)
            && string.IsNullOrEmpty(parsed.Query)
            && string.IsNullOrEmpty(parsed.Fragment)
            && parsed.AbsolutePath == "/")
        {
            origin = parsed;
            return true;
        }

        origin = null!;
        return false;
    }
}
