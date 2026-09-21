using FluentAssertions;
using Lodestone.Application.DTOs.Counselor;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Publishing availability is the only place a counselor writes a time window that students can
/// immediately act on, so the accepted window (future, 30-120 minutes, no overlap) is pinned at
/// both edges rather than only in the middle.
/// </summary>
public class CounselorAvailabilityServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    // ---------- Normal ----------

    [Fact]
    public async Task PublishAsync_StoresASlotForTheSignedInCounselorAndAudits()
    {
        CounselorAvailabilitySlot? stored = null;
        var bookings = CreateRepository();
        bookings.Setup(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()))
            .Callback<CounselorAvailabilitySlot, CancellationToken>((slot, _) => stored = slot)
            .Returns(Task.CompletedTask);
        var audit = new Mock<IAuditLogService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new CounselorAvailabilityService(bookings.Object, audit.Object, unitOfWork.Object);

        var start = DateTime.UtcNow.AddDays(3);
        await service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(50)));

        stored.Should().NotBeNull();
        stored!.CounselorProfileId.Should().Be(7);
        stored.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        audit.Verify(a => a.Record("AvailabilityPublished", nameof(CounselorAvailabilitySlot), null, It.IsAny<string>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsThePublishedSlotsWithTheirBookedState()
    {
        var bookings = CreateRepository();
        bookings.Setup(r => r.GetCounselorSlotsAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CounselorAvailabilitySlot { Id = 1, CounselorProfileId = 7, StartUtc = NowUtc, EndUtc = NowUtc.AddMinutes(50), IsBooked = true },
                new CounselorAvailabilitySlot { Id = 2, CounselorProfileId = 7, StartUtc = NowUtc.AddHours(2), EndUtc = NowUtc.AddHours(3) }
            });
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var page = await service.GetAsync("counselor-user");

        page.Should().NotBeNull();
        page!.CounselorName.Should().Be("Dr. Hasan");
        page.Slots.Should().HaveCount(2);
        page.Slots[0].IsBooked.Should().BeTrue();
    }

    // ---------- Boundary ----------

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    public async Task PublishAsync_AcceptsTheShortestAndLongestPermittedSession(int minutes)
    {
        var bookings = CreateRepository();
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var start = DateTime.UtcNow.AddDays(1);
        var publish = () => service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(minutes)));

        await publish.Should().NotThrowAsync();
        bookings.Verify(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(121)]
    public async Task PublishAsync_RejectsOneMinuteOutsideThePermittedLength(int minutes)
    {
        var bookings = CreateRepository();
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var start = DateTime.UtcNow.AddDays(1);
        var publish = () => service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(minutes)));

        await publish.Should().ThrowAsync<ArgumentException>();
        bookings.Verify(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Invalid ----------

    [Fact]
    public async Task PublishAsync_RejectsATimeThatHasAlreadyPassed()
    {
        var bookings = CreateRepository();
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var start = DateTime.UtcNow.AddHours(-2);
        var publish = () => service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(60)));

        await publish.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishAsync_RejectsAWindowThatEndsBeforeItStarts()
    {
        var bookings = CreateRepository();
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var start = DateTime.UtcNow.AddDays(1);
        var publish = () => service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(-60)));

        await publish.Should().ThrowAsync<ArgumentException>();
        bookings.Verify(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ConvertsALocalTimeToUtcBeforeStoringIt()
    {
        CounselorAvailabilitySlot? stored = null;
        var bookings = CreateRepository();
        bookings.Setup(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()))
            .Callback<CounselorAvailabilitySlot, CancellationToken>((slot, _) => stored = slot)
            .Returns(Task.CompletedTask);
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        // The publish form posts an ISO string with an offset, so the bound value arrives as a local
        // DateTime. The stored value must be the UTC instant, not the wall-clock reading.
        var localStart = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2), DateTimeKind.Utc).ToLocalTime();
        await service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(localStart, localStart.AddMinutes(60)));

        stored!.StartUtc.Should().Be(localStart.ToUniversalTime());
        stored.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---------- Exceptional ----------

    [Fact]
    public async Task PublishAsync_RefusesAWindowThatOverlapsAnAlreadyPublishedSlot()
    {
        var bookings = CreateRepository();
        bookings.Setup(r => r.HasOverlappingSlotAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), unitOfWork.Object);

        var start = DateTime.UtcNow.AddDays(1);
        var publish = () => service.PublishAsync("counselor-user", new PublishAvailabilitySlotDto(start, start.AddMinutes(60)));

        (await publish.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*overlaps*");
        bookings.Verify(r => r.AddSlotAsync(It.IsAny<CounselorAvailabilitySlot>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_RefusesAUserWhoHasNoCounselorProfile()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(r => r.GetCounselorByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounselorProfile?)null);
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        var start = DateTime.UtcNow.AddDays(1);
        var publish = () => service.PublishAsync("not-a-counselor", new PublishAvailabilitySlotDto(start, start.AddMinutes(60)));

        await publish.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAsync_ReturnsNothingForAUserWhoHasNoCounselorProfile()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(r => r.GetCounselorByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounselorProfile?)null);
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        (await service.GetAsync("not-a-counselor")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_ReportsNotFoundForAUserWhoHasNoCounselorProfile()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(r => r.GetCounselorByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounselorProfile?)null);
        var service = new CounselorAvailabilityService(bookings.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>());

        (await service.RemoveAsync("not-a-counselor", 5)).Should().Be(AvailabilityRemovalResult.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_LeavesABookedSlotInPlaceAndSavesNothing()
    {
        var bookings = CreateRepository();
        bookings.Setup(r => r.RemoveOwnedSlotAsync(7, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailabilityRemovalResult.Booked);
        var unitOfWork = new Mock<IUnitOfWork>();
        var audit = new Mock<IAuditLogService>();
        var service = new CounselorAvailabilityService(bookings.Object, audit.Object, unitOfWork.Object);

        var result = await service.RemoveAsync("counselor-user", 5);

        result.Should().Be(AvailabilityRemovalResult.Booked);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        audit.VerifyNoOtherCalls();
    }

    private static Mock<IBookingRepository> CreateRepository()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(r => r.GetCounselorByUserIdAsync("counselor-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorProfile { Id = 7, User = new ApplicationUser { FullName = "Dr. Hasan" } });
        bookings.Setup(r => r.HasOverlappingSlotAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        bookings.Setup(r => r.GetCounselorSlotsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CounselorAvailabilitySlot>());
        return bookings;
    }
}
