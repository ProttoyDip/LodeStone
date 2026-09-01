using Lodestone.Application.Interfaces;
using Lodestone.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Services;

/// <summary>
/// Sends a payload-free refresh signal to one recipient's authorized admin connections.
/// The signal carries no notification content: clients re-read their own unread count over an
/// authenticated endpoint, so nothing recipient-scoped travels over the hub.
/// </summary>
public sealed class SignalRAdminNotifier(
    IHubContext<AdminNotificationHub> hubContext,
    ILogger<SignalRAdminNotifier> logger) : IAdminNotificationNotifier
{
    public async Task NotifyChangedAsync(string recipientUserId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(recipientUserId))
            return;

        try
        {
            // Clients.User resolves through the default IUserIdProvider, which reads the
            // NameIdentifier claim — the ASP.NET Identity user id stored on Notification.
            await hubContext.Clients
                .User(recipientUserId)
                .SendCoreAsync("NotificationsChanged", Array.Empty<object>(), cancellationToken);
        }
        catch (Exception exception)
        {
            // The notification has already been persisted. A transient push failure must not
            // turn a successful write into a retry; the badge corrects on next page load.
            logger.LogWarning(
                exception,
                "Could not signal admin clients that notifications changed for recipient {RecipientUserId}.",
                recipientUserId);
        }
    }
}
