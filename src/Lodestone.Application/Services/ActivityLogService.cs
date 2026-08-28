using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IStudentProfileRepository _studentProfileRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ActivityLogService(IActivityLogRepository activityLogRepository, IStudentProfileRepository studentProfileRepository, IUnitOfWork unitOfWork)
    {
        _activityLogRepository = activityLogRepository;
        _studentProfileRepository = studentProfileRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task RecordLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var studentProfileId = await _studentProfileRepository.GetIdByUserIdAsync(userId, cancellationToken);
        if (!studentProfileId.HasValue) return;

        await _activityLogRepository.AddAsync(new Lodestone.Domain.Entities.ActivityLog
        {
            StudentProfileId = studentProfileId.Value,
            OccurredAtUtc = DateTime.UtcNow,
            LoginCount = 1
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
