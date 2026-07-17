using Legacy.Maliev.Intranet.Auth;

namespace Legacy.Maliev.Intranet.Tests;

internal sealed class TestAccessTokenValidator(
    bool acceptsToken = true,
    string employeeId = "employee-id") : ILegacyAccessTokenValidator
{
    public bool TryValidateEmployee(string accessToken, out EmployeeIdentity? identity)
    {
        identity = acceptsToken
            ? new EmployeeIdentity(employeeId, "employee@maliev.com", "employee@maliev.com")
            : null;
        return acceptsToken;
    }
}
