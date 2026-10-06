using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Pure serialization and ownership/version controls; not producer, session or joined-write evidence.</summary>
public sealed class CustomerAdministrationContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly string IdentityVersion = '"' + new string('A', 64) + '"';

    [Fact]
    public void Save_ExactWireExcludesAllIdentifiersAndProtectedSettings()
    {
        Assert.Equal(new[] { "dateOfBirth", "email", "emailConfirmed", "fax", "firstName", "identityVersion",
            "lastName", "lockoutEnabled", "mobile", "phoneNumberConfirmed", "profileVersion", "telephone" },
            Names(JsonSerializer.Serialize(Save(), Web)));
        Assert.Equal(Save(), JsonSerializer.Deserialize<CustomerAdministrationSaveRequest>(JsonSerializer.Serialize(Save(), Web), Web));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("identityId")]
    [InlineData("databaseID")]
    [InlineData("customerId")]
    [InlineData("userName")]
    [InlineData("twoFactorEnabled")]
    [InlineData("lockoutEnd")]
    [InlineData("accessFailedCount")]
    [InlineData("passwordHash")]
    [InlineData("securityStamp")]
    [InlineData("concurrencyStamp")]
    [InlineData("companyId")]
    [InlineData("billingAddressId")]
    [InlineData("shippingAddressId")]
    public void Save_RejectsUnknownOwnershipAndSecurityMembers(string member)
    {
        var valid = JsonSerializer.Serialize(Save(), Web);
        Assert.NotNull(JsonSerializer.Deserialize<CustomerAdministrationSaveRequest>(valid, Web));
        var document = JsonNode.Parse(valid)!.AsObject();
        document[member] = null;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CustomerAdministrationSaveRequest>(document.ToJsonString(), Web));
    }

    [Theory]
    [InlineData("emailConfirmed")]
    [InlineData("phoneNumberConfirmed")]
    [InlineData("lockoutEnabled")]
    [InlineData("profileVersion")]
    [InlineData("identityVersion")]
    public void Save_RequiresExplicitFlagsAndOriginalVersions(string member)
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(Save(), Web))!.AsObject();
        document.Remove(member);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CustomerAdministrationSaveRequest>(document.ToJsonString(), Web));
    }

    [Theory]
    [InlineData("emailConfirmed")]
    [InlineData("phoneNumberConfirmed")]
    [InlineData("twoFactorEnabled")]
    [InlineData("lockoutEnd")]
    [InlineData("lockoutEnabled")]
    public void IdentityRead_MissingSettingsCannotBeSilentlyDefaultedAndPreserved(string member)
    {
        var valid = JsonSerializer.Serialize(Identity(), Web);
        Assert.NotNull(JsonSerializer.Deserialize<CustomerAdministrationIdentity>(valid, Web));
        var document = JsonNode.Parse(valid)!.AsObject();
        document.Remove(member);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CustomerAdministrationIdentity>(document.ToJsonString(), Web));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("W/\"01234567\"")]
    [InlineData("\"short\"")]
    [InlineData("\"01234567\",\"01234568\"")]
    [InlineData(" \"01234567\"")]
    [InlineData("\"01234567\"\r\n")]
    public void Versions_RejectMissingWeakMultipleOrDecoratedValues(string? version)
    {
        Assert.False(CustomerAdministrationVersion.IsProfile(version));
        Assert.False(CustomerAdministrationVersion.IsIdentity(version));
    }

    [Fact]
    public void Versions_DistinctGrammarPreservesActualProducerLengthAndCasing()
    {
        Assert.True(CustomerAdministrationVersion.IsProfile("\"abcdef01\""));
        Assert.False(CustomerAdministrationVersion.IsProfile("\"ABCDEF01\""));
        Assert.True(CustomerAdministrationVersion.IsIdentity(IdentityVersion));
        Assert.False(CustomerAdministrationVersion.IsIdentity('"' + new string('a', 64) + '"'));
        Assert.False(CustomerAdministrationVersion.IsIdentity("\"abcdef01\""));
        Assert.False(CustomerAdministrationVersion.IsProfile(IdentityVersion));
    }

    [Theory]
    [InlineData("route-zero")]
    [InlineData("wrong-owner")]
    [InlineData("empty-id")]
    [InlineData("wrong-body-version")]
    [InlineData("null-identity")]
    public void Binding_RejectsWrongResourceOrBodyVersion(string defect)
    {
        var identity = Identity();
        var id = defect == "route-zero" ? 0 : 42;
        identity = defect switch
        {
            "wrong-owner" => identity with { DatabaseID = 43 },
            "empty-id" => identity with { Id = " " },
            "wrong-body-version" => identity with { Version = new string('B', 64) },
            _ => identity,
        };
        Assert.False(CustomerAdministrationVersion.IsBound(id, defect == "null-identity" ? null : identity, IdentityVersion));
    }

    [Fact]
    public void Mapping_PreservesSharedContactsSourceFlagsAndCapturedProtectedSettings()
    {
        var save = Save();
        var identity = Identity();
        Assert.True(CustomerAdministrationVersion.IsBound(42, identity, IdentityVersion));
        var update = save.ToIdentity(42, identity);
        Assert.Equal(save.Email, update.UserName);
        Assert.Equal(save.Email, update.Email);
        Assert.Equal(save.Telephone, update.PhoneNumber);
        Assert.Equal(save.Mobile, update.MobileNumber);
        Assert.Equal(save.Fax, update.FaxNumber);
        Assert.Equal(save.EmailConfirmed, update.EmailConfirmed);
        Assert.Equal(save.PhoneNumberConfirmed, update.PhoneNumberConfirmed);
        Assert.Equal(save.LockoutEnabled, update.LockoutEnabled);
        Assert.Equal(identity.TwoFactorEnabled, update.TwoFactorEnabled);
        Assert.Equal(identity.LockoutEnd, update.LockoutEnd);
        var profile = save.ToProfile();
        Assert.Equal(save.FirstName, profile.FirstName);
        Assert.Equal(save.LastName, profile.LastName);
        Assert.Equal(save.Email, profile.Email);
        Assert.Equal(save.Telephone, profile.Telephone);
        Assert.Equal(save.Mobile, profile.Mobile);
        Assert.Equal(save.Fax, profile.Fax);
        Assert.Equal(save.DateOfBirth, profile.DateOfBirth);
        Assert.Equal(new[] { "dateOfBirth", "email", "fax", "firstName", "lastName", "mobile", "telephone" }, Names(JsonSerializer.Serialize(profile, Web)));
        Assert.Equal(new[] { "email", "emailConfirmed", "faxNumber", "lockoutEnabled", "lockoutEnd", "mobileNumber",
            "phoneNumber", "phoneNumberConfirmed", "twoFactorEnabled", "userName" }, Names(JsonSerializer.Serialize(update, Web)));
    }

    [Fact]
    public void Mapping_WrongOwnerOrReplacedIdentityVersionCannotBuildAnUpdate()
    {
        Assert.Throws<ArgumentException>(() => Save().ToIdentity(43, Identity()));
        Assert.Throws<ArgumentException>(() => Save().ToIdentity(42, Identity() with { Version = new string('B', 64) }));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("databaseID")]
    [InlineData("version")]
    [InlineData("accessFailedCount")]
    [InlineData("passwordHash")]
    [InlineData("securityStamp")]
    [InlineData("concurrencyStamp")]
    public void IdentityUpdate_RejectsReadOnlyOrSecurityMembers(string member)
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(Save().ToIdentity(42, Identity()), Web))!.AsObject();
        document[member] = null;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CustomerAdministrationIdentityUpdate>(document.ToJsonString(), Web));
    }

    [Fact]
    public void Save_UsesExistingProfileValidationLengthsAndEmailRules()
    {
        var valid = Save();
        Assert.True(Validator.TryValidateObject(valid, new(valid), [], true));
        var invalid = Save() with { FirstName = new string('a', 257), Email = "not-an-email", Mobile = new string('0', 65) };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new(invalid), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(invalid.FirstName)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(invalid.Email)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(invalid.Mobile)));
    }

    [Theory]
    [InlineData("apostrophe")]
    [InlineData("unicode")]
    [InlineData("too-long")]
    public void SharedEmail_RejectsBothProducerPolicyConflictsBeforeEitherWrite(string defect)
    {
        var email = defect switch
        {
            "apostrophe" => "customer'name@example.invalid",
            "unicode" => "ลูกค้า@example.invalid",
            _ => new string('a', 241) + "@example.invalid",
        };
        Assert.True(new EmailAddressAttribute().IsValid(email));
        var request = Save() with { Email = email };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new(request), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Email)));
    }

    [Theory]
    [InlineData("customer+tag@example.invalid")]
    [InlineData("customer-name@example.invalid")]
    public void SharedEmail_AllowsTheActualAuthUsernameAlphabet(string email)
    {
        var request = Save() with { Email = email };
        Assert.True(Validator.TryValidateObject(request, new(request), [], true));
    }

    [Fact]
    public void SharedEmail_AllowsCustomerStorageBoundaryOf256Characters()
    {
        var request = Save() with { Email = new string('a', 240) + "@example.invalid" };
        Assert.Equal(256, request.Email.Length);
        Assert.True(Validator.TryValidateObject(request, new(request), [], true));
    }

    private static CustomerAdministrationSaveRequest Save() => new("FIRST", "LAST", "customer@example.invalid",
        "+66812345678", "MOBILE", "FAX", new DateTime(1990, 1, 2), false, true, false, "\"abcdef01\"", IdentityVersion);

    private static CustomerAdministrationIdentity Identity() => new("customer-identity", "old@example.invalid",
        "old@example.invalid", true, "+66811111111", false, true,
        new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), true, 3, 42, "OLD-FAX", "OLD-MOBILE", new string('A', 64));

    private static string[] Names(string json) => JsonNode.Parse(json)!.AsObject().Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
}
