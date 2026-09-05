using FluentAssertions;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class VolunteerSupportServiceTests
{
    [Fact]
    public async Task CreateSupportRequestAsync_CreatesRequestForEligibleStudent()
    {
        var repo = new Mock<IVolunteerSupportRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var audit = new Mock<IAuditLogService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.UserId).Returns("student-42");

        var student = new StudentProfile { Id = 7, UserId = "student-42", User = new ApplicationUser { Id = "student-42", FullName = "Student A" } };
        var volunteer = new VolunteerProfile { Id = 4, UserId = "vol-1", IsApproved = true, IsActive = true, User = new ApplicationUser { Id = "vol-1", FullName = "Volunteer A" } };

        repo.Setup(r => r.GetStudentProfileByUserIdAsync("student-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(student);
        repo.Setup(r => r.GetAvailableVolunteersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { volunteer });
        repo.Setup(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new VolunteerSupportService(repo.Object, currentUser.Object, unitOfWork.Object, audit.Object);

        var result = await service.CreateSupportRequestAsync(
            new CreateSupportRequestDto(
                SupportRequestCategory.AcademicGuidance,
                "Need help with coursework",
                "I need advice on choosing an elective and planning my schedule."),
            CancellationToken.None);

        result.Category.Should().Be(SupportRequestCategory.AcademicGuidance);
        result.Status.Should().Be(SupportRequestStatus.Open);
        repo.Verify(r => r.AddSupportRequestAsync(It.IsAny<SupportRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
