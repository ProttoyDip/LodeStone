using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Lodestone.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Lodestone.Web.ViewModels.Auth;
using Xunit;

namespace Lodestone.UnitTests.Web;

/// <summary>
/// The privacy-consent checkbox on the registration form. Its rule is enforced on the server and
/// must also emit a client rule the browser can actually evaluate: a rule that fails for a ticked
/// box blocks the submit entirely, so nobody can register.
/// </summary>
public class RegistrationConsentValidationTests
{
    [Fact]
    public void Registration_IsRejectedWhenThePrivacyCommitmentIsNotAccepted()
    {
        var results = Validate(NewModel(acceptPrivacy: false));

        results.Should().ContainSingle(result =>
            result.MemberNames.Contains(nameof(RegisterViewModel.AcceptPrivacy)));
    }

    [Fact]
    public void Registration_IsAcceptedWhenThePrivacyCommitmentIsAccepted()
        => Validate(NewModel(acceptPrivacy: true)).Should().BeEmpty();

    [Fact]
    public void ConsentRule_EmitsTheRequiredClientRuleRatherThanAnUnusableRangeRule()
    {
        // data-val-range on a checkbox compares the string "true" against the string "True" in
        // jQuery validation and fails for a ticked box, which silently blocks every submission.
        var attributes = new Dictionary<string, string>();
        new MustBeAcceptedAttribute { ErrorMessage = "Please accept." }.AddValidation(ClientContext(attributes));

        attributes.Should().ContainKey("data-val-required").WhoseValue.Should().Be("Please accept.");
        attributes.Keys.Should().NotContain(key => key.StartsWith("data-val-range", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    [InlineData("true", false)]
    public void ConsentRule_AcceptsOnlyABooleanTrue(object? value, bool expected)
        => new MustBeAcceptedAttribute().IsValid(value).Should().Be(expected);

    private static ClientModelValidationContext ClientContext(IDictionary<string, string> attributes)
    {
        var provider = new EmptyModelMetadataProvider();
        var metadata = provider.GetMetadataForProperty(typeof(RegisterViewModel), nameof(RegisterViewModel.AcceptPrivacy));
        return new ClientModelValidationContext(new ActionContext(), metadata, provider, attributes);
    }

    private static RegisterViewModel NewModel(bool acceptPrivacy)
        => new()
        {
            FullName = "Lab Six Tester",
            Email = "lab6@test.local",
            StudentNumber = "L600001",
            Password = "Lab6Testing!2026",
            ConfirmPassword = "Lab6Testing!2026",
            AcceptPrivacy = acceptPrivacy
        };

    private static List<ValidationResult> Validate(RegisterViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
