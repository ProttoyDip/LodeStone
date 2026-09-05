namespace Lodestone.Application.Interfaces;

/// <summary>
/// Framework-neutral notification boundary for refreshing authorized admin clients.
/// Implementations must not include notification content in the payload: the signal only tells a
/// client its unread count may have changed, and the client re-reads that count over an
/// authorized endpoint. This keeps recipient-scoped data out of the transport layer.
/// </summary>
public interface IAdminNotificationNotifier
{
    /// <summary>Signals one recipient that their unread notification count may have changed.</summary>
    Task NotifyChangedAsync(string recipientUserId, CancellationToken cancellationToken = default);
}
