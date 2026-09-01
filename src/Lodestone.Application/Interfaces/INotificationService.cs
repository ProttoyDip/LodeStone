using Lodestone.Domain.Enums;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Creates recipient-scoped notifications and reports unread counts. Writing a notification also
/// signals the recipient's connected clients through <see cref="IAdminNotificationNotifier"/>.
/// </summary>
public interface INotificationService
{
    /// <summary>Notifies a single recipient. Returns the created notification identifier.</summary>
    Task<int> CreateAsync(
        string recipientUserId,
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies every user in the administrator role. Used for queue-style events that any
    /// administrator may action, such as a student number claim awaiting review.
    /// </summary>
    Task<int> NotifyAdministratorsAsync(
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Unread count for the signed-in user; zero when unauthenticated.</summary>
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
}
