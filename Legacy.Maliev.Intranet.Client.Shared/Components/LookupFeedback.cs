using System.Net;
using Legacy.Maliev.Intranet.Client.Shared.Infrastructure;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Client.Shared.Components;

public static class LookupFeedback
{
    public static string For(Exception error, IStringLocalizer<LookupResources> text) => text[error is LookupRequestException request
        ? request.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.BadRequest => "Invalid",
            HttpStatusCode.TooManyRequests => "RateLimited",
            HttpStatusCode.UnprocessableEntity => "Unsupported",
            _ => "Unavailable"
        } : "Unavailable"];
}
