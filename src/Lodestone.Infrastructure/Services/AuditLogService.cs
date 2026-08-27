using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;

namespace Lodestone.Infrastructure.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AuditLogService(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public void Record(string action, string? entityName = null, string? entityId = null, string? details = null)
    {
        var userId = _currentUserService.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUserService.UserId)
            ? _currentUserService.UserId
            : null;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            TimestampUtc = DateTime.UtcNow
        });
    }
}
