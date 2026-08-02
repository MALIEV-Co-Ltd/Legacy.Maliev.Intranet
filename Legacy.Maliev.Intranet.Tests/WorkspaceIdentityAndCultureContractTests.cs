using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class WorkspaceIdentityAndCultureContractTests
{
    [Theory]
    [InlineData("employee@maliev.com")]
    [InlineData("EMPLOYEE@MALIEV.COM")]
    [InlineData("employee+intranet@maliev.com")]
    public void WorkspaceEmailPolicy_AllowsOnlyTheCorporateDomain(string email)
    {
        Assert.True(WorkspaceIdentityRules.IsAllowedEmployeeEmail(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("employee@example.com")]
    [InlineData("employee@sub.maliev.com")]
    [InlineData("employee @maliev.com")]
    [InlineData("employee@@maliev.com")]
    public void WorkspaceEmailPolicy_RejectsNonCorporateOrMalformedAddresses(string? email)
    {
        Assert.False(WorkspaceIdentityRules.IsAllowedEmployeeEmail(email));
    }

    [Fact]
    public void WorkspaceEmailPolicy_AllowsLocalFixtureDomainOnlyWhenExplicitlyEnabled()
    {
        Assert.False(WorkspaceIdentityRules.IsAllowedEmployeeEmail("local.employee@maliev.test"));
        Assert.True(WorkspaceIdentityRules.IsAllowedEmployeeEmail("local.employee@maliev.test", allowLocalTestDomain: true));
        Assert.False(WorkspaceIdentityRules.IsAllowedEmployeeEmail("employee@example.com", allowLocalTestDomain: true));
    }

    [Fact]
    public void WorkspaceCulture_UsesTheCurrentSupportedLanguageContract()
    {
        Assert.Equal("en-TH", WorkspaceCulture.DefaultCultureName);
        Assert.Equal(["en-TH", "th-TH", "en-US"], WorkspaceCulture.SupportedCultureNames);
    }

    [Theory]
    [InlineData(null, "en-TH")]
    [InlineData("", "en-TH")]
    [InlineData("unknown", "en-TH")]
    [InlineData("th-th", "th-TH")]
    [InlineData(" EN-us ", "en-US")]
    public void WorkspaceCulture_NormalizesUnknownAndCaseVariantValues(string? value, string expected)
    {
        Assert.Equal(expected, WorkspaceCulture.Normalize(value));
        Assert.Equal(expected, WorkspaceCulture.GetCulture(value).Name);
    }

}

public sealed class EmployeeSignInRememberMeContractTests
{
    [Fact]
    public void EmployeeSignInRequest_ExposesRememberMeWithoutChangingExistingWireFields()
    {
        var request = new EmployeeSignInRequest("employee@maliev.com", "password", "/Dashboard", true);
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var payload = JsonDocument.Parse(json);

        Assert.Equal("employee@maliev.com", payload.RootElement.GetProperty("email").GetString());
        Assert.Equal("password", payload.RootElement.GetProperty("password").GetString());
        Assert.Equal("/Dashboard", payload.RootElement.GetProperty("returnUrl").GetString());
        Assert.True(payload.RootElement.GetProperty("rememberMe").GetBoolean());
    }
}
