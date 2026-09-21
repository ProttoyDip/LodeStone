using FluentAssertions;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Rejected and exceptional booking paths. <see cref="BookingServiceTests"/> covers the accepted
/// path; these cases assert that bad input is refused before it reaches the database and that a
/// refusal never writes an audit row or a transaction.
/// </summary>
public class BookingServiceEdgeCaseTests
{
    // ---------- Invalid student input ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task CreateBookingAsync_RefusesANonPositiveSlotWithoutTouchingTheRepository(int slotId)
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var book = () => service.CreateBookingAsync(9, new CreateBookingDto(slotId, null));

        await book.Should().ThrowAsync<ArgumentException>();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBookingAsync_RefusesNotesLongerThanAThousandCharacters()
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var book = () => service.CreateBookingAsync(9, new CreateBookingDto(14, new string('x', 1001)));

        await book.Should().ThrowAsync<ArgumentException>();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBookingAsync_AcceptsNotesExactlyAtTheLimitAndTrimsThem()
    {
        var notes = new string('x', 1000);
        var repository = new Mock<IBookingRepository>();
        repository.Setup(r => r.TryCreateConfirmedAsync(9, 14, notes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewBooking());
        var service = CreateService(repository);

        var result = await service.CreateBookingAsync(9, new CreateBookingDto(14, $"  {notes}  "));

        result.Id.Should().Be(22);
        repository.Verify(r => r.TryCreateConfirmedAsync(9, 14, notes, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Exceptional cancellation ----------

    [Theory]
    [InlineData(BookingCancellationResult.NotFound)]
    [InlineData(BookingCancellationResult.NotCancellable)]
    public async Task CancelAsync_ReportsARefusalWithoutAuditingOrSaving(BookingCancellationResult refusal)
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(r => r.CancelOwnedAsync(9, 22, It.IsAny<CancellationToken>())).ReturnsAsync(refusal);
        var audit = new Mock<IAuditLogService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new BookingService(repository.Object, audit.Object, unitOfWork.Object, TimeProvider.System);

        var result = await service.CancelAsync(9, 22);

        result.Should().Be(refusal);
        audit.VerifyNoOtherCalls();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Counselor outcome recording ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordCounselorOutcomeAsync_RefusesARequestWithNoSignedInCounselor(string userId)
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var result = await service.RecordCounselorOutcomeAsync(userId, 5, BookingStatus.Completed, null);

        result.Should().Be(CounselorBookingUpdateResult.InvalidRequest);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task RecordCounselorOutcomeAsync_RefusesANonPositiveBooking(int bookingId)
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var result = await service.RecordCounselorOutcomeAsync("counselor-user", bookingId, BookingStatus.Completed, null);

        result.Should().Be(CounselorBookingUpdateResult.InvalidRequest);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData((BookingStatus)99)]
    public async Task RecordCounselorOutcomeAsync_AcceptsOnlyCompletedOrNoShow(BookingStatus outcome)
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var result = await service.RecordCounselorOutcomeAsync("counselor-user", 5, outcome, null);

        result.Should().Be(CounselorBookingUpdateResult.InvalidRequest);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordCounselorOutcomeAsync_RefusesSessionNotesPastTheTwoThousandCharacterLimit()
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var result = await service.RecordCounselorOutcomeAsync(
            "counselor-user", 5, BookingStatus.Completed, new string('n', 2001));

        result.Should().Be(CounselorBookingUpdateResult.InvalidRequest);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordCounselorOutcomeAsync_RefusesAnUnfilledTemplateSoAnEmptyDraftIsNeverFiled()
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        var result = await service.RecordCounselorOutcomeAsync(
            "counselor-user",
            5,
            BookingStatus.Completed,
            $"What we discussed: {SessionReportDrafter.Placeholder}");

        result.Should().Be(CounselorBookingUpdateResult.InvalidRequest);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecordCounselorOutcomeAsync_ReportsNotFoundWhenTheUserHasNoCounselorProfile()
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(r => r.GetCounselorByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounselorProfile?)null);
        var service = new BookingService(repository.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>(), TimeProvider.System);

        var result = await service.RecordCounselorOutcomeAsync("someone-else", 5, BookingStatus.Completed, null);

        result.Should().Be(CounselorBookingUpdateResult.NotFound);
    }

    [Fact]
    public async Task RecordCounselorOutcomeAsync_DoesNotAuditARefusalFromTheRepository()
    {
        var repository = new Mock<IBookingRepository>();
        repository.Setup(r => r.GetCounselorByUserIdAsync("counselor-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CounselorProfile { Id = 4 });
        repository.Setup(r => r.RecordCounselorOutcomeAsync(
                4, "counselor-user", 5, BookingStatus.NoShow, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CounselorBookingUpdateResult.NotFound);
        var audit = new Mock<IAuditLogService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new BookingService(repository.Object, audit.Object, unitOfWork.Object, TimeProvider.System);

        var result = await service.RecordCounselorOutcomeAsync("counselor-user", 5, BookingStatus.NoShow, "  ");

        result.Should().Be(CounselorBookingUpdateResult.NotFound);
        audit.VerifyNoOtherCalls();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Missing identity on the workspace read ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCounselorAppointmentsAsync_ReturnsNothingForABlankIdentity(string userId)
    {
        var repository = new Mock<IBookingRepository>();
        var service = CreateService(repository);

        (await service.GetCounselorAppointmentsAsync(userId)).Should().BeNull();
        repository.VerifyNoOtherCalls();
    }

    private static BookingService CreateService(Mock<IBookingRepository> repository)
        => new(repository.Object, Mock.Of<IAuditLogService>(), Mock.Of<IUnitOfWork>(), TimeProvider.System);

    private static CounselorBooking NewBooking()
        => new()
        {
            Id = 22,
            StudentProfileId = 9,
            CounselorProfileId = 4,
            ScheduledForUtc = DateTime.UtcNow.AddDays(2),
            Status = BookingStatus.Confirmed
        };
}
