using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public class ActivityLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordLoginAsync_DelegatesToAtomicConsentGatedRepository()
    {
        var activities = new Mock<IActivityLogRepository>();
        activities.Setup(value => value.RecordLoginIfConsentedAsync(
                "student-user",
                Now.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new ActivityLogService(activities.Object, new FixedTimeProvider(Now));

        await service.RecordLoginAsync(" student-user ");

        activities.Verify(value => value.RecordLoginIfConsentedAsync(
            "student-user",
            Now.UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordLoginAsync_IgnoresMissingUserIdentifier()
    {
        var activities = new Mock<IActivityLogRepository>();
        var service = new ActivityLogService(activities.Object, new FixedTimeProvider(Now));

        await service.RecordLoginAsync("  ");

        activities.Verify(value => value.RecordLoginIfConsentedAsync(
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
