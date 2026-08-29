namespace Lodestone.Application.Interfaces;

public interface IActivityLogRepository
{
    /// <summary>Atomically records a login only while the student has active monitoring consent.</summary>
    Task<bool> RecordLoginIfConsentedAsync(
        string userId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);
}
