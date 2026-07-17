using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Legacy.Maliev.Intranet.Client;

/// <summary>Projects the BFF cookie session into Blazor route authorization state.</summary>
public sealed class EmployeeAuthenticationStateProvider(EmployeeSessionClient sessionClient)
    : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var session = await sessionClient.GetAsync();
        if (session?.IsAuthenticated != true || string.IsNullOrWhiteSpace(session.EmployeeId))
        {
            return Anonymous;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.EmployeeId),
            new("identity_kind", "employee"),
        };
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            claims.Add(new Claim(ClaimTypes.Name, session.DisplayName));
        }

        claims.AddRange(session.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "BffCookie",
            ClaimTypes.Name,
            ClaimTypes.Role)));
    }
}
