using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Employees;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeCreationEmailValidationTests
{
    [Theory]
    [InlineData(255, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void BothCreationContracts_MatchOriginalProfileStorageBoundary(int length, bool expected)
    {
        var email = new string('a', length - "@fixture.invalid".Length) + "@fixture.invalid";
        object[] inputs =
        [
            new CreateEmployeeAccountRequest
            {
                FirstName = "Synthetic", LastName = "Fixture", Email = email,
                Password = "fixture-only-password", ConfirmPassword = "fixture-only-password",
            },
            new CreateEmployeeInput
            {
                FirstName = "Synthetic", LastName = "Fixture", Email = email,
                Password = "fixture-only-password", ConfirmPassword = "fixture-only-password",
            },
        ];
        foreach (var input in inputs)
        {
            var errors = new List<ValidationResult>();
            Assert.Equal(expected, Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
            if (expected) Assert.Empty(errors);
            else Assert.Equal(["Email"], Assert.Single(errors).MemberNames);
        }
    }
}
