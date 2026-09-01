using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>EF-backed private support-prompt queries with ownership-scoped booking lookup.</summary>
public sealed class NudgeRepository : INudgeRepository
{
    private readonly ApplicationDbContext _context;

    public NudgeRepository(ApplicationDbContext context) => _context = context;

    public Task<StudentProfile?> GetStudentByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => _context.StudentProfiles
            .Include(profile => profile.NudgePreference)
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Nudge>> GetActiveForStudentAsync(
        int studentProfileId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => await _context.Nudges
            .AsNoTracking()
            .Where(nudge => nudge.StudentProfileId == studentProfileId
                            && nudge.AvailableAtUtc <= nowUtc
                            && nudge.ExpiresAtUtc > nowUtc
                            && (nudge.Status == NudgeStatus.Pending
                                || nudge.Status == NudgeStatus.Sent
                                || nudge.Status == NudgeStatus.Snoozed))
            .OrderByDescending(nudge => nudge.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Nudge?> GetActionableAsync(
        int studentProfileId,
        int nudgeId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => _context.Nudges.SingleOrDefaultAsync(
            nudge => nudge.Id == nudgeId
                     && nudge.StudentProfileId == studentProfileId
                     && nudge.AvailableAtUtc <= nowUtc
                     && nudge.ExpiresAtUtc > nowUtc
                     && (nudge.Status == NudgeStatus.Pending
                         || nudge.Status == NudgeStatus.Sent
                         || nudge.Status == NudgeStatus.Snoozed),
            cancellationToken);

    public Task<bool> HasManualNudgeSinceAsync(
        int studentProfileId,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
        => _context.Nudges.AnyAsync(
            nudge => nudge.StudentProfileId == studentProfileId
                     && nudge.IsManualCounselorNudge
                     && nudge.CreatedAtUtc >= sinceUtc,
            cancellationToken);

    public Task<CounselorBooking?> GetOwnedBookingAsync(
        int counselorProfileId,
        int bookingId,
        CancellationToken cancellationToken = default)
        => _context.CounselorBookings
            .Include(booking => booking.StudentProfile)
                .ThenInclude(profile => profile!.NudgePreference)
            .SingleOrDefaultAsync(
                booking => booking.Id == bookingId
                           && booking.CounselorProfileId == counselorProfileId
                           && (booking.Status == BookingStatus.Confirmed
                               || booking.Status == BookingStatus.Completed),
                cancellationToken);

    public Task AddAsync(Nudge nudge, CancellationToken cancellationToken = default)
        => _context.Nudges.AddAsync(nudge, cancellationToken).AsTask();

    public async Task<IReadOnlyList<Nudge>> GetPendingDispatchAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
        => await _context.Nudges
            .Include(nudge => nudge.StudentProfile)
                .ThenInclude(profile => profile!.NudgePreference)
            .Where(nudge => nudge.Status == NudgeStatus.Pending
                            && nudge.AvailableAtUtc <= nowUtc
                            && nudge.ExpiresAtUtc > nowUtc
                            && nudge.StudentProfile!.NudgePreference != null
                            && nudge.StudentProfile.NudgePreference.IsInAppNudgesEnabled)
            .ToListAsync(cancellationToken);
}
