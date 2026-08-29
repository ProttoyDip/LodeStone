namespace Lodestone.Application.Interfaces;

public interface IActivityLogService
{
    Task RecordLoginAsync(string userId, CancellationToken cancellationToken = default);
}
