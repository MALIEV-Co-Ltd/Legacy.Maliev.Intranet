using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeAdministrationContractTests
{
    [Theory]
    [InlineData("{\"lockoutEnd\":null}")]
    [InlineData("{\"twoFactorEnabled\":false}")]
    [InlineData("{\"twoFactorEnabled\":null,\"lockoutEnd\":null}")]
    public void SafeIdentityRead_RequiresProtectedSettingsBeforePreservingThem(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeAdministrationIdentity>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("W/\"version\"")]
    [InlineData("\"short\"")]
    [InlineData("\"a\",\"b\"")]
    public void CapturedVersions_RejectMissingWeakWildcardOrMultipleValues(string? version)
    {
        Assert.False(EmployeeAdministrationVersion.IsIdentity(version));
        Assert.False(EmployeeAdministrationVersion.IsEmployee(version));
    }

    [Fact]
    public void CapturedVersions_PreserveDistinctProducerCasingWithoutRecomputation()
    {
        var identity = '"' + new string('A', 64) + '"';
        var employee = '"' + new string('a', 64) + '"';
        Assert.True(EmployeeAdministrationVersion.IsIdentity(identity));
        Assert.False(EmployeeAdministrationVersion.IsEmployee(identity));
        Assert.True(EmployeeAdministrationVersion.IsEmployee(employee));
        Assert.False(EmployeeAdministrationVersion.IsIdentity(employee));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("databaseID")]
    [InlineData("accessFailedCount")]
    [InlineData("version")]
    [InlineData("passwordHash")]
    [InlineData("securityStamp")]
    [InlineData("concurrencyStamp")]
    public void IdentityWrite_RejectsReadOnlyAndSecurityMembers(string member)
    {
        var json = "{\"userName\":\"editor@example.invalid\",\"email\":\"editor@example.invalid\",\"" + member + "\":null}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeAdministrationIdentityUpdate>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("FullName")]
    [InlineData("CreatedDate")]
    [InlineData("ModifiedDate")]
    [InlineData("Password")]
    public void ProfileWrite_RejectsServerOwnedAndIdentityMembers(string member)
    {
        var json = "{\"FirstName\":\"FIRST\",\"LastName\":\"LAST\",\"Email\":\"editor@example.invalid\",\"" + member + "\":null}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeAdministrationProfileUpdate>(json));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("EmployeeId")]
    [InlineData("CreatedDate")]
    [InlineData("ModifiedDate")]
    public void AddressWrite_RejectsBrowserSelectableOwnerAndServerOwnedFields(string member)
    {
        var json = "{\"CountryId\":1,\"" + member + "\":null}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeAdministrationAddressUpdate>(json));
    }

    [Fact]
    public void Writes_HaveExactDistinctProducerFieldSetsAndNamingPolicies()
    {
        var identity = new EmployeeAdministrationIdentityUpdate("editor@example.invalid", "editor@example.invalid",
            false, null, false, false, null, true);
        var profile = new EmployeeAdministrationProfileUpdate(null, "FIRST", "LAST", null,
            "editor@example.invalid", null, null);
        var address = new EmployeeAdministrationAddressUpdate(null, "LINE", null, null, null, null, 1);
        Assert.Equal(new[] { "email", "emailConfirmed", "lockoutEnabled", "lockoutEnd", "phoneNumber",
                "phoneNumberConfirmed", "twoFactorEnabled", "userName" },
            Names(JsonSerializer.Serialize(identity, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal(new[] { "DateOfBirth", "Email", "FirstName", "HomeAddressId", "LastName", "PhoneNumber", "RoleId" },
            Names(JsonSerializer.Serialize(profile)));
        Assert.Equal(new[] { "AddressLine1", "AddressLine2", "Building", "City", "CountryId", "PostalCode", "State" },
            Names(JsonSerializer.Serialize(address)));
    }

    [Fact]
    public void BoundAddressWrite_ContainsOnlyDerivedRelationAndProducerScalarFields()
    {
        var bound = new EmployeeAdministrationBoundAddressUpdate(77, null, "LINE", null,
            "Bangkok", null, null, 66);
        Assert.Equal(new[] { "AddressId", "AddressLine1", "AddressLine2", "Building", "City",
                "CountryId", "PostalCode", "State" }, Names(JsonSerializer.Serialize(bound)));
    }

    [Theory]
    [InlineData("AddressId")]
    [InlineData("EmployeeId")]
    [InlineData("IdentityId")]
    [InlineData("TwoFactorEnabled")]
    [InlineData("LockoutEnd")]
    public void BrowserSave_RejectsProducerRelationAndProtectedIdentityFields(string member)
    {
        var json = "{\"firstName\":\"FIRST\",\"lastName\":\"LAST\",\"email\":\"editor@example.invalid\",\""
            + member + "\":null}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmployeeAdministrationSaveRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string[] Names(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
    }
}
