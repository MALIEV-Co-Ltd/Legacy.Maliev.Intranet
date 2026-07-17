using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client;

internal sealed class EmployeeAuthenticationClient(
    HttpClient httpClient,
    EmployeeSessionClient sessionClient,
    ILogger<EmployeeAuthenticationClient> logger)
{
    public async Task<EmployeeSignInClientResult> SignInAsync(
        string email,
        string password,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionClient.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session?.CsrfToken))
        {
            return EmployeeSignInClientResult.Failure("Employee sign-in is temporarily unavailable.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new EmployeeSignInRequest(email, password, returnUrl)),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var result = await response.Content.ReadFromJsonAsync<EmployeeSignInResponse>(cancellationToken);
                return result is not null && IsLocalPath(result.RedirectUrl)
                    ? EmployeeSignInClientResult.Success(result.RedirectUrl)
                    : EmployeeSignInClientResult.Failure("Employee sign-in returned an invalid response.");
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => EmployeeSignInClientResult.Failure(
                    "Check the email and password and try again."),
                HttpStatusCode.Unauthorized => EmployeeSignInClientResult.Failure(
                    "The email or password is invalid."),
                HttpStatusCode.TooManyRequests => EmployeeSignInClientResult.Failure(
                    "Too many sign-in attempts. Wait a minute and try again."),
                _ => EmployeeSignInClientResult.Failure("Employee sign-in is temporarily unavailable."),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "The same-origin employee sign-in request is unavailable.");
            return EmployeeSignInClientResult.Failure("Employee sign-in is temporarily unavailable.");
        }
    }

    private static bool IsLocalPath(string path) =>
        path.StartsWith("/", StringComparison.Ordinal) &&
        (path.Length == 1 || path[1] is not ('/' or '\\'));
}

internal sealed record EmployeeSignInClientResult(bool Succeeded, string? RedirectUrl, string? ErrorMessage)
{
    public static EmployeeSignInClientResult Success(string redirectUrl) => new(true, redirectUrl, null);

    public static EmployeeSignInClientResult Failure(string errorMessage) => new(false, null, errorMessage);
}
