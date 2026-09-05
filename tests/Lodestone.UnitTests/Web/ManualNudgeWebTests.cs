using FluentAssertions;
using Lodestone.Application.DTOs.Nudges;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Lodestone.ML.Models;
using Lodestone.Web.Controllers;
using Lodestone.Web.ViewModels.Risk;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Web;

public sealed class ManualNudgeWebTests
{
    [Theory]
    [InlineData(typeof(StudentController), nameof(StudentController.UpdateInAppNudgePreference))]
    [InlineData(typeof(StudentController), nameof(StudentController.RespondToNudge))]
    [InlineData(typeof(CounselorController), nameof(CounselorController.CreateManualNudge))]
    public void Manual_nudge_mutations_are_post_only_and_antiforgery_protected(
        Type controllerType,
        string actionName)
    {
        var action = controllerType.GetMethod(actionName);

        action.Should().NotBeNull();
        action!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Should().ContainSingle();
        action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).Should().ContainSingle();
    }

    [Fact]
    public async Task Student_preference_update_uses_the_current_student_and_redirects_back_to_the_prompt_section()
    {
        var nudges = new Mock<INudgeService>();
        nudges.Setup(service => service.SetInAppPreferenceAsync("student-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NudgeMutationResult.Updated);
        var controller = CreateStudentController("student-1", nudges.Object);

        var result = await controller.UpdateInAppNudgePreference(false, CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.Should().Match<RedirectToActionResult>(redirect =>
                redirect.ActionName == nameof(StudentController.Index)
                && redirect.Fragment == "support-prompts");
        controller.TempData["StudentNudgeSuccess"].Should().BeOfType<string>()
            .Which.Should().Contain("off");
        nudges.Verify(service => service.SetInAppPreferenceAsync("student-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Student_invalid_prompt_response_does_not_reach_the_service()
    {
        var nudges = new Mock<INudgeService>();
        var controller = CreateStudentController("student-1", nudges.Object);
        controller.ModelState.AddModelError("action", "Invalid response action.");

        var result = await controller.RespondToNudge(22, NudgeResponseAction.Acknowledge, CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["StudentNudgeError"].Should().BeOfType<string>()
            .Which.Should().Contain("not recognised");
        nudges.Verify(
            service => service.RespondAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<NudgeResponseAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Counselor_prompt_creation_uses_a_fixed_template_and_the_current_counselor()
    {
        var nudges = new Mock<INudgeService>();
        nudges.Setup(service => service.CreateManualForBookingAsync(
                "counselor-1",
                18,
                ManualNudgeTemplate.SupportResources,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NudgeMutationResult.Updated);
        var controller = CreateCounselorController("counselor-1", nudges.Object);

        var result = await controller.CreateManualNudge(
            18,
            ManualNudgeTemplate.SupportResources,
            CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(CounselorController.Reviews));
        controller.TempData["ManualNudgeSuccess"].Should().BeOfType<string>()
            .Which.Should().Contain("independent of risk monitoring");
        nudges.Verify(service => service.CreateManualForBookingAsync(
            "counselor-1",
            18,
            ManualNudgeTemplate.SupportResources,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Counselor_invalid_template_does_not_reach_the_service()
    {
        var nudges = new Mock<INudgeService>();
        var controller = CreateCounselorController("counselor-1", nudges.Object);
        controller.ModelState.AddModelError("template", "Invalid template.");

        var result = await controller.CreateManualNudge(18, ManualNudgeTemplate.CheckIn, CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["ManualNudgeError"].Should().BeOfType<string>()
            .Which.Should().Contain("approved neutral prompt templates");
        nudges.Verify(
            service => service.CreateManualForBookingAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<ManualNudgeTemplate>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Staff_runtime_status_keeps_the_loaded_model_schema_when_scoring_status_is_unavailable()
    {
        var model = new RiskRuntimeStatusViewModel
        {
            ModelStatus = RiskModelStatus.Available("withdrawal-28d-v2-model", "withdrawal-28d-v2"),
            StatusError = "Latest scoring status unavailable."
        };

        model.FeatureSchemaVersion.Should().Be("withdrawal-28d-v2");
    }

    private static StudentController CreateStudentController(string userId, INudgeService nudges)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.UserId).Returns(userId);
        
        // For tests of old methods (nudges), context is not used
        // Using null is safe since these tests don't exercise the new volunteer support paths
        return WithTempData(new StudentController(
            Mock.Of<IStudentDashboardService>(),
            Mock.Of<IRiskMonitoringConsentService>(),
            Mock.Of<IStudentNumberVerificationService>(),
            current.Object,
            nudges,
            Mock.Of<IVolunteerSupportService>(),
            null!,  // ApplicationDbContext not used by nudge-related methods
            NullLogger<StudentController>.Instance));
    }

    private static CounselorController CreateCounselorController(string userId, INudgeService nudges)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.UserId).Returns(userId);
        return WithTempData(new CounselorController(
            Mock.Of<ICounselorQueueService>(),
            Mock.Of<IBookingService>(),
            nudges,
            current.Object,
            Mock.Of<IRiskSnapshotAdministrationService>(),
            Mock.Of<IRiskModelStatusProvider>(),
            NullLogger<CounselorController>.Instance));
    }

    private static TController WithTempData<TController>(TController controller)
        where TController : Controller
    {
        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>());
        return controller;
    }
}
