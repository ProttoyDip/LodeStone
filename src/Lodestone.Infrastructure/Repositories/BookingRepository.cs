using Lodestone.Application.Interfaces;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Counselor;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>Booking-specific queries: student bookings, counselor bookings, slots, counselor list.</summary>
public class BookingRepository : GenericRepository<CounselorBooking>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext context) : base(context) { }

    public async Task<IReadOnlyList<CounselorBooking>> GetByStudentIdAsync(
        int studentProfileId, CancellationToken cancellationToken = default)
        => await Set
            .Include(b => b.CounselorProfile)
                .ThenInclude(c => c!.User)
            .Include(b => b.AvailabilitySlot)
            .Where(b => b.StudentProfileId == studentProfileId)
            .OrderByDescending(b => b.ScheduledForUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CounselorBooking>> GetByCounselorIdAsync(
        int counselorProfileId, CancellationToken cancellationToken = default)
        => await Set
            .Include(b => b.StudentProfile)
            .Where(b => b.CounselorProfileId == counselorProfileId
                     && b.ScheduledForUtc >= DateTime.UtcNow)
            .OrderBy(b => b.ScheduledForUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CounselorBooking>> GetCounselorWorkspaceAsync(
        int counselorProfileId,
        DateTime recentFromUtc,
        CancellationToken cancellationToken = default)
        => await Set
            .Include(booking => booking.StudentProfile)
                .ThenInclude(profile => profile!.User)
            .Include(booking => booking.AvailabilitySlot)
            .Include(booking => booking.SessionReport)
            .Where(booking => booking.CounselorProfileId == counselorProfileId
                              && (booking.Status == BookingStatus.Confirmed
                                  || (booking.ScheduledForUtc >= recentFromUtc
                                      && (booking.Status == BookingStatus.Completed
                                          || booking.Status == BookingStatus.NoShow
                                          || booking.Status == BookingStatus.Cancelled))))
            .OrderBy(booking => booking.ScheduledForUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CounselorAvailabilitySlot>> GetAvailableSlotsAsync(
        int? counselorProfileId, CancellationToken cancellationToken = default)
        => await Context.CounselorAvailabilitySlots
            .Include(s => s.CounselorProfile)
                .ThenInclude(c => c!.User)
            .Where(s => (!counselorProfileId.HasValue || s.CounselorProfileId == counselorProfileId.Value)
                     && !s.IsBooked
                     && s.StartUtc > DateTime.UtcNow
                     && s.CounselorProfile != null
                     && s.CounselorProfile.IsAcceptingBookings
                     && s.CounselorProfile.User != null
                     && s.CounselorProfile.User.IsActive)
            .OrderBy(s => s.StartUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CounselorProfile>> GetAllCounselorsAsync(
        CancellationToken cancellationToken = default)
        => await Context.CounselorProfiles
            .Include(c => c.User)
            .Where(c => c.IsAcceptingBookings && c.User != null && c.User.IsActive)
            .OrderBy(c => c.User != null ? c.User.FullName : string.Empty)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<CounselorBooking?> TryCreateConfirmedAsync(
        int studentProfileId,
        int slotId,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var reserved = await Context.CounselorAvailabilitySlots
            .Where(slot => slot.Id == slotId
                           && !slot.IsBooked
                           && slot.StartUtc > nowUtc
                           && slot.CounselorProfile != null
                           && slot.CounselorProfile.IsAcceptingBookings)
            .ExecuteUpdateAsync(setters => setters.SetProperty(slot => slot.IsBooked, true), cancellationToken);

        if (reserved != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var slot = await Context.CounselorAvailabilitySlots
            .Include(item => item.CounselorProfile)
                .ThenInclude(profile => profile!.User)
            .SingleAsync(item => item.Id == slotId, cancellationToken);

        var booking = new CounselorBooking
        {
            StudentProfileId = studentProfileId,
            CounselorProfileId = slot.CounselorProfileId,
            AvailabilitySlotId = slot.Id,
            AvailabilitySlot = slot,
            CounselorProfile = slot.CounselorProfile,
            ScheduledForUtc = slot.StartUtc,
            Notes = notes,
            Status = BookingStatus.Confirmed,
            CreatedAtUtc = nowUtc
        };

        await Set.AddAsync(booking, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return booking;
    }

    public async Task<BookingCancellationResult> CancelOwnedAsync(
        int studentProfileId,
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);
        var booking = await Set
            .Include(item => item.AvailabilitySlot)
            .SingleOrDefaultAsync(item => item.Id == bookingId && item.StudentProfileId == studentProfileId, cancellationToken);

        if (booking is null)
            return BookingCancellationResult.NotFound;

        if (booking.Status != BookingStatus.Confirmed || booking.ScheduledForUtc <= DateTime.UtcNow)
            return BookingCancellationResult.NotCancellable;

        booking.Status = BookingStatus.Cancelled;
        booking.ModifiedAtUtc = DateTime.UtcNow;
        if (booking.AvailabilitySlot is not null)
            booking.AvailabilitySlot.IsBooked = false;

        await Context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BookingCancellationResult.Cancelled;
    }

    public Task<CounselorProfile?> GetCounselorByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        => Context.CounselorProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<CounselorAvailabilitySlot>> GetCounselorSlotsAsync(
        int counselorProfileId,
        CancellationToken cancellationToken = default)
        => await Context.CounselorAvailabilitySlots
            .Where(slot => slot.CounselorProfileId == counselorProfileId && slot.EndUtc > DateTime.UtcNow)
            .OrderBy(slot => slot.StartUtc)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<bool> HasOverlappingSlotAsync(
        int counselorProfileId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
        => Context.CounselorAvailabilitySlots.AnyAsync(
            slot => slot.CounselorProfileId == counselorProfileId
                    && startUtc < slot.EndUtc
                    && endUtc > slot.StartUtc,
            cancellationToken);

    public Task AddSlotAsync(CounselorAvailabilitySlot slot, CancellationToken cancellationToken = default)
        => Context.CounselorAvailabilitySlots.AddAsync(slot, cancellationToken).AsTask();

    public async Task<AvailabilityRemovalResult> RemoveOwnedSlotAsync(
        int counselorProfileId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var slot = await Context.CounselorAvailabilitySlots
            .SingleOrDefaultAsync(item => item.Id == slotId && item.CounselorProfileId == counselorProfileId, cancellationToken);
        if (slot is null)
            return AvailabilityRemovalResult.NotFound;
        if (slot.IsBooked)
            return AvailabilityRemovalResult.Booked;

        Context.CounselorAvailabilitySlots.Remove(slot);
        await Context.SaveChangesAsync(cancellationToken);
        return AvailabilityRemovalResult.Removed;
    }

    public async Task<CounselorBookingUpdateResult> RecordCounselorOutcomeAsync(
        int counselorProfileId,
        string counselorUserId,
        int bookingId,
        BookingStatus outcome,
        string? sessionNotes,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var booking = await Set
            .Include(item => item.SessionReport)
            .SingleOrDefaultAsync(
                item => item.Id == bookingId && item.CounselorProfileId == counselorProfileId,
                cancellationToken);

        if (booking is null) return CounselorBookingUpdateResult.NotFound;
        if (booking.Status != BookingStatus.Confirmed || booking.ScheduledForUtc > nowUtc)
            return CounselorBookingUpdateResult.NotEligible;

        booking.Status = outcome;
        booking.ModifiedAtUtc = nowUtc;
        booking.ModifiedBy = counselorUserId;

        if (!string.IsNullOrWhiteSpace(sessionNotes))
        {
            if (booking.SessionReport is null)
            {
                booking.SessionReport = new CounselorSessionReport
                {
                    CounselorBookingId = booking.Id,
                    Summary = sessionNotes,
                    Status = ReportStatus.Submitted,
                    CreatedAtUtc = nowUtc,
                    CreatedBy = counselorUserId
                };
            }
            else
            {
                booking.SessionReport.Summary = sessionNotes;
                booking.SessionReport.Status = ReportStatus.Submitted;
                booking.SessionReport.ModifiedAtUtc = nowUtc;
                booking.SessionReport.ModifiedBy = counselorUserId;
            }
        }

        return CounselorBookingUpdateResult.Updated;
    }
}
