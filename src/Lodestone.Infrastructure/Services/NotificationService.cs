using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

/// <summary>
/// Persists notifications, then signals the recipient's connected clients. The realtime signal is
/// raised only after the write commits, and a transport failure never fails the caller: the
/// notification is already durable and the badge corrects itself on the next page load.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaximumTitleLength = 200;
    private const int MaximumMessageLength = 1000;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdminNotificationNotifier _notifier;

    public NotificationService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        IAdminNotificationNotifier notifier)
    {
        _context = context;
        _userManager = userManager;
        _currentUserService = currentUserService;
        _notifier = notifier;
    }

    public async Task<int> CreateAsync(
        string recipientUserId,
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var notification = new Notification
        {
            RecipientUserId = recipientUserId.Trim(),
            Type = type,
            Title = Truncate(title, MaximumTitleLength),
            Message = Truncate(message ?? string.Empty, MaximumMessageLength),
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);
        await SignalAsync(notification.RecipientUserId, cancellationToken);
        return notification.Id;
    }

    public async Task<int> NotifyAdministratorsAsync(
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var administrators = await _userManager.GetUsersInRoleAsync(RoleConstants.Admin);
        if (administrators.Count == 0) return 0;

        var createdAtUtc = DateTime.UtcNow;
        var safeTitle = Truncate(title, MaximumTitleLength);
        var safeMessage = Truncate(message ?? string.Empty, MaximumMessageLength);

        var notifications = administrators
            .Where(administrator => !string.IsNullOrWhiteSpace(administrator.Id))
            .Select(administrator => new Notification
            {
                RecipientUserId = administrator.Id,
                Type = type,
                Title = safeTitle,
                Message = safeMessage,
                IsRead = false,
                CreatedAtUtc = createdAtUtc
            })
            .ToArray();
        if (notifications.Length == 0) return 0;

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
            await SignalAsync(notification.RecipientUserId, cancellationToken);

        return notifications.Length;
    }

    public async Task<int> NotifyAdministratorsOnceAsync(
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var administrators = await _userManager.GetUsersInRoleAsync(RoleConstants.Admin);
        if (administrators.Count == 0) return 0;

        var safeTitle = Truncate(title, MaximumTitleLength);
        var recipientIds = administrators
            .Select(administrator => administrator.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (recipientIds.Length == 0) return 0;

        // An administrator who has not yet read the previous alert does not need another one.
        var alreadyWaiting = await _context.Notifications
            .AsNoTracking()
            .Where(notification => !notification.IsRead &&
                                   notification.Type == type &&
                                   notification.Title == safeTitle &&
                                   recipientIds.Contains(notification.RecipientUserId))
            .Select(notification => notification.RecipientUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var pending = recipientIds
            .Except(alreadyWaiting, StringComparer.Ordinal)
            .ToArray();
        if (pending.Length == 0) return 0;

        var createdAtUtc = DateTime.UtcNow;
        var safeMessage = Truncate(message ?? string.Empty, MaximumMessageLength);
        var notifications = pending
            .Select(recipientUserId => new Notification
            {
                RecipientUserId = recipientUserId,
                Type = type,
                Title = safeTitle,
                Message = safeMessage,
                IsRead = false,
                CreatedAtUtc = createdAtUtc
            })
            .ToArray();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
            await SignalAsync(notification.RecipientUserId, cancellationToken);

        return notifications.Length;
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUserService.UserId))
            return 0;

        var userId = _currentUserService.UserId;
        return await _context.Notifications
            .AsNoTracking()
            .CountAsync(
                notification => notification.RecipientUserId == userId && !notification.IsRead,
                cancellationToken);
    }

    private async Task SignalAsync(string recipientUserId, CancellationToken cancellationToken)
    {
        // The notification row has committed. A push failure must not surface as a failed write.
        try
        {
            await _notifier.NotifyChangedAsync(recipientUserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Swallowed by design; the notifier logs its own transport failures.
        }
    }

    private static string Truncate(string value, int maximumLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
