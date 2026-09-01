using FluentAssertions;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public class BookingServiceTests
{
    [Fact]
    public async Task CreateBookingAsync_UsesTheReservedSlotAndReturnsConfirmedBooking()
    {
        var start = DateTime.UtcNow.AddDays(2);
        var repository = new Mock<IBookingRepository>();
        repository.Setup(value => value.TryCreateConfirmedAsync(9, 14, "Please discuss workload", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorBooking
            {
                Id = 22,
                StudentProfileId = 9,
                CounselorProfileId = 4,
                CounselorProfile = new CounselorProfile { Id = 4, User = new ApplicationUser { FullName = "Dr. Rahman" }, Specialization = "Academic stress" },
                AvailabilitySlot = new CounselorAvailabilitySlot { StartUtc = start, EndUtc = start.AddMinutes(50) },
                ScheduledForUtc = start,
                Status = BookingStatus.Confirmed
            });
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await new BookingService(repository.Object, Mock.Of<IAuditLogService>(), unitOfWork.Object, TimeProvider.System)
            .CreateBookingAsync(9, new CreateBookingDto(14, "Please discuss workload"));

        result.Id.Should().Be(22);
        result.CounselorName.Should().Be("Dr. Rahman");
        result.Status.Should().Be(BookingStatus.Confirmed);
        result.StartUtc.Should().Be(start);
    }

    [Fact]
    public async Task CreateBookingAsync_ReportsAConcurrentReservationAsUnavailable()
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(value => value.TryCreateConfirmedAsync(9, 14, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounselorBooking?)null);
        var service = new BookingService(repository.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>(), TimeProvider.System);

        var action = () => service.CreateBookingAsync(9, new CreateBookingDto(14, null));

        await action.Should().ThrowAsync<BookingSlotUnavailableException>();
    }

    [Fact]
    public async Task CancelAsync_ScopesCancellationToTheOwningStudent()
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(value => value.CancelOwnedAsync(9, 22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BookingCancellationResult.Cancelled);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await new BookingService(repository.Object, Mock.Of<IAuditLogService>(), unitOfWork.Object, TimeProvider.System).CancelAsync(9, 22);

        result.Should().Be(BookingCancellationResult.Cancelled);
        repository.Verify(value => value.CancelOwnedAsync(9, 22, It.IsAny<CancellationToken>()), Times.Once);
    }
}
