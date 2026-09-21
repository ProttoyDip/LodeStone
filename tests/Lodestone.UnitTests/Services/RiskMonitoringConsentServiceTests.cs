using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Consent is the switch that decides whether a student is monitored at all, so an absent or
/// malformed identity must fail loudly instead of reading or writing someone else's row.
/// </summary>
public class RiskMonitoringConsentServiceTests
{
    [Fact]
    public async Task SetAsync_RecordsTheDecisionAgainstTheStudentAsTheirOwnActor()
    {
        var repository = new Mock<IRiskMonitoringConsentRepository>();
        repository.Setup(r => r.SetByUserIdAsync("student-1", true, "student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskMonitoringConsentDto(1, true, "v1", DateTime.UtcNow, null));
        var service = new RiskMonitoringConsentService(repository.Object);

        var result = await service.SetAsync("student-1", true);

        result.IsConsented.Should().BeTrue();
        repository.Verify(r => r.SetByUserIdAsync("student-1", true, "student-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_TrimsTheIdentityBeforeItReachesStorage()
    {
        var repository = new Mock<IRiskMonitoringConsentRepository>();
        repository.Setup(r => r.SetByUserIdAsync("student-1", false, "student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskMonitoringConsentDto(1, false, "v1", null, DateTime.UtcNow));
        var service = new RiskMonitoringConsentService(repository.Object);

        await service.SetAsync("  student-1  ", false);

        repository.Verify(r => r.SetByUserIdAsync("student-1", false, "student-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_RefusesAnAbsentIdentityWithoutReadingAnything(string? userId)
    {
        var repository = new Mock<IRiskMonitoringConsentRepository>();
        var service = new RiskMonitoringConsentService(repository.Object);

        var read = () => service.GetAsync(userId!);

        await read.Should().ThrowAsync<ArgumentException>();
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetAsync_RefusesAnAbsentIdentityWithoutWritingAnything(string? userId)
    {
        var repository = new Mock<IRiskMonitoringConsentRepository>();
        var service = new RiskMonitoringConsentService(repository.Object);

        var write = () => service.SetAsync(userId!, true);

        await write.Should().ThrowAsync<ArgumentException>();
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task GetSummaryAsync_RefusesAnAbsentIdentity(string? userId)
    {
        var service = new RiskMonitoringConsentService(Mock.Of<IRiskMonitoringConsentRepository>());

        var read = () => service.GetSummaryAsync(userId!);

        await read.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_ReturnsNothingForAUserWhoHasNoStudentProfile()
    {
        var repository = new Mock<IRiskMonitoringConsentRepository>();
        repository.Setup(r => r.GetByUserIdAsync("staff-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RiskMonitoringConsentDto?)null);
        var service = new RiskMonitoringConsentService(repository.Object);

        (await service.GetAsync("staff-1")).Should().BeNull();
    }
}
