using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public class ActivityLogServiceTests
{
    [Fact]
    public async Task RecordLoginAsync_PersistsAStudentScopedLoginEvent()
    {
        ActivityLog? captured = null;
        var profiles = new Mock<IStudentProfileRepository>();
        profiles.Setup(value => value.GetIdByUserIdAsync("student-user", It.IsAny<CancellationToken>())).ReturnsAsync(7);
        var activities = new Mock<IActivityLogRepository>();
        activities.Setup(value => value.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((item, _) => captured = item)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await new ActivityLogService(activities.Object, profiles.Object, unitOfWork.Object).RecordLoginAsync("student-user");

        captured.Should().NotBeNull();
        captured!.StudentProfileId.Should().Be(7);
        captured.LoginCount.Should().Be(1);
        captured.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordLoginAsync_IgnoresUsersWithoutAStudentProfile()
    {
        var profiles = new Mock<IStudentProfileRepository>();
        profiles.Setup(value => value.GetIdByUserIdAsync("counselor", It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        var activities = new Mock<IActivityLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        await new ActivityLogService(activities.Object, profiles.Object, unitOfWork.Object).RecordLoginAsync("counselor");

        activities.Verify(value => value.AddAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
