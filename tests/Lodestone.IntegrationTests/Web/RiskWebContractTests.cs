using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Student;
using Lodestone.Domain.Enums;
using Lodestone.ML.Models;
using Lodestone.Web.Controllers;
using Lodestone.Web.ViewModels.Admin;
using Lodestone.Web.ViewModels.Auth;
using Lodestone.Web.ViewModels.Student;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Lodestone.IntegrationTests.Web;

public sealed class RiskWebContractTests
{
    [Fact]
    public void Registration_requires_an_importable_student_number()
    {
        var model = ValidRegistration();
        model.StudentNumber = string.Empty;

        var errors = Validate(model);

        errors.Should().Contain(error =>
            error.MemberNames.Contains(nameof(RegisterViewModel.StudentNumber)));
    }

    [Theory]
    [InlineData("STU 100")]
    [InlineData("#100")]
    [InlineData("STU@100")]
    public void Registration_rejects_student_numbers_outside_the_import_contract(string studentNumber)
    {
        var model = ValidRegistration();
        model.StudentNumber = studentNumber;

        Validate(model).Should().Contain(error =>
            error.MemberNames.Contains(nameof(RegisterViewModel.StudentNumber)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("STU 100")]
    [InlineData("#100")]
    public void Existing_student_claim_uses_the_same_identifier_contract(string studentNumber)
    {
        var model = new StudentNumberClaimViewModel { StudentNumber = studentNumber };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true)
            .Should().BeFalse();
        results.Should().Contain(error =>
            error.MemberNames.Contains(nameof(StudentNumberClaimViewModel.StudentNumber)));
    }

    [Fact]
    public void Run_now_is_enabled_only_for_a_loaded_model_with_pending_snapshots()
    {
        var descriptor = new RiskModelDescriptor("model-v1", "withdrawal-28d-v1", 28, .5);
        var viewModel = new RiskOperationsViewModel
        {
            ModelStatus = RiskModelStatus.Available("model-v1"),
            SnapshotStatus = new RiskSnapshotStatusDto(3, 1, 2, DateTime.UtcNow, descriptor, null, null)
        };

        viewModel.CanRunScoring.Should().BeTrue();
    }

    [Fact]
    public void Run_now_stays_disabled_when_the_model_is_unavailable()
    {
        var viewModel = new RiskOperationsViewModel
        {
            ModelStatus = RiskModelStatus.Unavailable("Artifact missing."),
            SnapshotStatus = new RiskSnapshotStatusDto(3, 1, 0, DateTime.UtcNow, null, "Artifact missing.", null)
        };

        viewModel.CanRunScoring.Should().BeFalse();
        viewModel.ModelUnavailableReason.Should().Be("Artifact missing.");
    }

    [Theory]
    [InlineData(StudentNumberClaimStatus.Pending, "Pending", false)]
    [InlineData(StudentNumberClaimStatus.Rejected, "Rejected", true)]
    public void Student_privacy_state_reflects_the_latest_unverified_claim(
        StudentNumberClaimStatus status,
        string expectedLabel,
        bool canSubmit)
    {
        var claim = Claim(status);
        var state = new StudentNumberVerificationStateDto(17, null, claim);

        var viewModel = new StudentHomeViewModel(Dashboard(), null, state);

        viewModel.StudentNumberStateLabel.Should().Be(expectedLabel);
        viewModel.DisplayStudentNumber.Should().Be("STU-100");
        viewModel.CanSubmitStudentNumber.Should().Be(canSubmit);
    }

    [Fact]
    public void Verified_student_number_is_read_only_even_when_the_latest_claim_is_rejected()
    {
        var state = new StudentNumberVerificationStateDto(
            17,
            "STU-100",
            Claim(StudentNumberClaimStatus.Rejected));

        var viewModel = new StudentHomeViewModel(Dashboard(), null, state);

        viewModel.StudentNumberStateLabel.Should().Be("Verified");
        viewModel.DisplayStudentNumber.Should().Be("STU-100");
        viewModel.CanSubmitStudentNumber.Should().BeFalse();
    }

    [Fact]
    public void Monitoring_consent_remains_independent_while_identity_is_pending()
    {
        var consent = new RiskMonitoringConsentDto(17, true, "risk-monitoring-v1", DateTime.UtcNow, null);
        var state = new StudentNumberVerificationStateDto(17, null, Claim(StudentNumberClaimStatus.Pending));

        var viewModel = new StudentHomeViewModel(Dashboard(), consent, state);

        viewModel.IsRiskMonitoringEnabled.Should().BeTrue();
        viewModel.IsStudentNumberVerified.Should().BeFalse();
        viewModel.CanSubmitStudentNumber.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(StudentController), nameof(StudentController.SubmitStudentNumber))]
    [InlineData(typeof(AdminController), nameof(AdminController.ApproveStudentNumberClaim))]
    [InlineData(typeof(AdminController), nameof(AdminController.RejectStudentNumberClaim))]
    [InlineData(typeof(AdminController), nameof(AdminController.ResetVerifiedStudentNumber))]
    public void Student_number_mutations_are_post_only_and_antiforgery_protected(
        Type controllerType,
        string actionName)
    {
        var action = controllerType.GetMethod(actionName);

        action.Should().NotBeNull();
        action!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Should().ContainSingle();
        action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).Should().ContainSingle();
    }

    [Theory]
    [InlineData(nameof(AdminController.ApproveStudentNumberClaim))]
    [InlineData(nameof(AdminController.RejectStudentNumberClaim))]
    public void Admin_claim_reviews_require_a_row_version_token(string actionName)
    {
        var action = typeof(AdminController).GetMethod(actionName);

        action.Should().NotBeNull();
        action!.GetParameters().Should().Contain(parameter =>
            parameter.Name == "rowVersionToken" && parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void Risk_operations_lists_are_safe_when_no_identity_records_exist()
    {
        var viewModel = new RiskOperationsViewModel
        {
            ModelStatus = RiskModelStatus.Unavailable("Disabled")
        };

        viewModel.PendingStudentNumberClaims.Should().BeEmpty();
        viewModel.VerifiedStudentNumbers.Should().BeEmpty();
    }

    private static IReadOnlyList<ValidationResult> Validate(RegisterViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static RegisterViewModel ValidRegistration() => new()
    {
        FullName = "Alex Rahman",
        Email = "alex@example.edu",
        StudentNumber = "STU-100",
        Password = "password123",
        ConfirmPassword = "password123",
        AcceptPrivacy = true
    };

    private static StudentNumberClaimDto Claim(StudentNumberClaimStatus status)
        => new(
            7,
            17,
            "Alex Rahman",
            "alex@example.edu",
            "STU-100",
            status,
            DateTime.UtcNow.AddMinutes(-5),
            status == StudentNumberClaimStatus.Pending ? null : DateTime.UtcNow,
            status == StudentNumberClaimStatus.Pending ? null : "admin-1",
            Convert.ToBase64String([1, 2, 3, 4]));

    private static StudentDashboardDto Dashboard()
        => new(
            "Alex",
            false,
            0,
            0,
            0,
            0,
            [new StudentActivityDayDto(DateTime.UtcNow.Date, 0)],
            null,
            new StudentRecommendationDto(
                "Start here",
                "Take a pause",
                "A short reflection can help.",
                "Journal",
                "Index",
                "Open journal"));
}
