using FluentAssertions;
using Lodestone.Application.DTOs.Nudges;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class NudgeServiceTests
{
    [Fact]
    public async Task GetForStudentAsync_TreatsAnAbsentPreferenceAsAnExplicitOptOut()
    {
        var nudges = new Mock<INudgeRepository>();
        nudges.Setup(repository => repository.GetStudentByUserIdAsync("student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentProfile { Id = 10, UserId = "student-1" });
        var fixture = CreateFixture(nudges: nudges);

        var result = await fixture.Service.GetForStudentAsync("student-1");

        result.Should().NotBeNull();
        result!.IsInAppNudgesEnabled.Should().BeFalse();
        result.ActiveNudges.Should().BeEmpty();
        nudges.Verify(repository => repository.GetActiveForStudentAsync(
            It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateManualForBookingAsync_RequiresTheStudentsSeparatePromptOptIn()
    {
        var nudges = new Mock<INudgeRepository>();
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(repository => repository.GetCounselorByUserIdAsync("counselor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorProfile { Id = 7, UserId = "counselor-1" });
        nudges.Setup(repository => repository.GetOwnedBookingAsync(7, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorBooking
            {
                Id = 8,
                CounselorProfileId = 7,
                StudentProfileId = 10,
                Status = BookingStatus.Confirmed,
                StudentProfile = new StudentProfile { Id = 10, UserId = "student-1" }
            });
        var fixture = CreateFixture(nudges, bookings);

        var result = await fixture.Service.CreateManualForBookingAsync(
            "counselor-1", 8, ManualNudgeTemplate.CheckIn);

        result.Should().Be(NudgeMutationResult.PreferenceDisabled);
        nudges.Verify(repository => repository.AddAsync(It.IsAny<Nudge>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetInAppPreferenceAsync_CreatesAnAuditedExplicitOptIn()
    {
        var nudges = new Mock<INudgeRepository>();
        var student = new StudentProfile { Id = 10, UserId = "student-1" };
        nudges.Setup(repository => repository.GetStudentByUserIdAsync("student-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(student);
        var fixture = CreateFixture(nudges: nudges);

        var result = await fixture.Service.SetInAppPreferenceAsync("student-1", true);

        result.Should().Be(NudgeMutationResult.Updated);
        student.NudgePreference.Should().NotBeNull();
        student.NudgePreference!.IsInAppNudgesEnabled.Should().BeTrue();
        fixture.Audit.Verify(audit => audit.Record(
            "NudgePreference.Enabled",
            nameof(StudentNudgePreference),
            "10",
            It.IsAny<string?>()), Times.Once);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture CreateFixture(
        Mock<INudgeRepository>? nudges = null,
        Mock<IBookingRepository>? bookings = null)
    {
        var fixture = new Fixture
        {
            Nudges = nudges ?? new Mock<INudgeRepository>(),
            Bookings = bookings ?? new Mock<IBookingRepository>()
        };
        fixture.Service = new NudgeService(
            fixture.Nudges.Object,
            fixture.Bookings.Object,
            fixture.UnitOfWork.Object,
            fixture.Audit.Object,
            new FixedTimeProvider());
        return fixture;
    }

    private sealed class Fixture
    {
        public Mock<INudgeRepository> Nudges { get; init; } = null!;
        public Mock<IBookingRepository> Bookings { get; init; } = null!;
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IAuditLogService> Audit { get; } = new();
        public NudgeService Service { get; set; } = null!;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    }
}
