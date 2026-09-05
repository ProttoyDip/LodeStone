using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

/// <summary>Persistence operations for private, in-app support prompts.</summary>
public interface INudgeRepository
{
    Task<StudentProfile?> GetStudentByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Nudge>> GetActiveForStudentAsync(int studentProfileId, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<Nudge?> GetActionableAsync(int studentProfileId, int nudgeId, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> HasManualNudgeSinceAsync(int studentProfileId, DateTime sinceUtc, CancellationToken cancellationToken = default);
    Task<CounselorBooking?> GetOwnedBookingAsync(int counselorProfileId, int bookingId, CancellationToken cancellationToken = default);
    Task AddAsync(Nudge nudge, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Nudge>> GetPendingDispatchAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
