using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly TimeProvider _timeProvider;

    public ActivityLogService(
        IActivityLogRepository activityLogRepository,
        TimeProvider timeProvider)
    {
        _activityLogRepository = activityLogRepository;
        _timeProvider = timeProvider;
    }

    public async Task RecordLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        await _activityLogRepository.RecordLoginIfConsentedAsync(
            userId.Trim(),
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }
}
