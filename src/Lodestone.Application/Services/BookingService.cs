using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IUnitOfWork _unitOfWork;

    public BookingService(IBookingRepository bookingRepository, IAuditLogService auditLogService, IUnitOfWork unitOfWork)
    {
        _bookingRepository = bookingRepository;
        _auditLogService = auditLogService;
        _unitOfWork = unitOfWork;
    }

    public async Task<BookingDto> CreateBookingAsync(int studentProfileId, CreateBookingDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.AvailabilitySlotId <= 0 || dto.Notes?.Length > 1000)
            throw new ArgumentException("Select an available appointment and keep notes within 1,000 characters.", nameof(dto));

        var booking = await _bookingRepository.TryCreateConfirmedAsync(studentProfileId, dto.AvailabilitySlotId, dto.Notes?.Trim(), cancellationToken)
            ?? throw new BookingSlotUnavailableException();
        _auditLogService.Record("BookingCreated", nameof(CounselorBooking), booking.Id.ToString(), "Confirmed from a published slot.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(booking);
    }

    public async Task<IReadOnlyList<BookingDto>> GetStudentBookingsAsync(int studentProfileId, CancellationToken cancellationToken = default)
        => (await _bookingRepository.GetByStudentIdAsync(studentProfileId, cancellationToken)).Select(MapToDto).ToList().AsReadOnly();

    public async Task<IReadOnlyList<BookingDto>> GetUpcomingAsync(int counselorProfileId, CancellationToken cancellationToken = default)
        => (await _bookingRepository.GetByCounselorIdAsync(counselorProfileId, cancellationToken)).Select(MapToDto).ToList().AsReadOnly();

    public async Task<IReadOnlyList<CounselorSummaryDto>> GetCounselorsAsync(CancellationToken cancellationToken = default)
        => (await _bookingRepository.GetAllCounselorsAsync(cancellationToken))
            .Select(c => new CounselorSummaryDto(c.Id, string.IsNullOrWhiteSpace(c.User?.FullName) ? $"Counselor #{c.Id}" : c.User.FullName, c.Specialization))
            .ToList().AsReadOnly();

    public async Task<IReadOnlyList<BookingSlotDto>> GetAvailableSlotsAsync(int? counselorProfileId = null, CancellationToken cancellationToken = default)
        => (await _bookingRepository.GetAvailableSlotsAsync(counselorProfileId, cancellationToken))
            .Select(s => new BookingSlotDto(
                s.Id,
                s.CounselorProfileId,
                string.IsNullOrWhiteSpace(s.CounselorProfile?.User?.FullName) ? $"Counselor #{s.CounselorProfileId}" : s.CounselorProfile.User.FullName,
                s.CounselorProfile?.Specialization,
                s.StartUtc,
                s.EndUtc))
            .ToList().AsReadOnly();

    public async Task<BookingCancellationResult> CancelAsync(int studentProfileId, int bookingId, CancellationToken cancellationToken = default)
    {
        var result = await _bookingRepository.CancelOwnedAsync(studentProfileId, bookingId, cancellationToken);
        if (result == BookingCancellationResult.Cancelled)
        {
            _auditLogService.Record("BookingCancelled", nameof(CounselorBooking), bookingId.ToString(), "Cancelled by the owning student.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return result;
    }

    private static BookingDto MapToDto(CounselorBooking b)
        => new(
            b.Id,
            b.CounselorProfileId,
            string.IsNullOrWhiteSpace(b.CounselorProfile?.User?.FullName) ? $"Counselor #{b.CounselorProfileId}" : b.CounselorProfile.User.FullName,
            b.CounselorProfile?.Specialization,
            b.ScheduledForUtc,
            b.AvailabilitySlot?.EndUtc ?? b.ScheduledForUtc.AddMinutes(50),
            b.Status,
            b.Status == BookingStatus.Confirmed && b.ScheduledForUtc > DateTime.UtcNow);
}
