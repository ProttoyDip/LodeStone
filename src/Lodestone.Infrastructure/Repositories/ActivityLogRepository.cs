using Lodestone.Infrastructure.Data;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>Persistence for first-party student activity events.</summary>
public class ActivityLogRepository : Lodestone.Application.Interfaces.IActivityLogRepository
{
    private readonly ApplicationDbContext _context;

    public ActivityLogRepository(ApplicationDbContext context) => _context = context;

    public Task AddAsync(Lodestone.Domain.Entities.ActivityLog activity, CancellationToken cancellationToken = default)
        => _context.ActivityLogs.AddAsync(activity, cancellationToken).AsTask();
}
