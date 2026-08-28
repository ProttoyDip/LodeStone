using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

public interface IActivityLogRepository
{
    Task AddAsync(ActivityLog activity, CancellationToken cancellationToken = default);
}
