using Lodestone.Application.Interfaces;
using Lodestone.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Services;

/// <summary>
/// Broadcasts a payload-free "the roster changed" signal over the admin hub.
/// Broadcasting rather than addressing a user is safe here only because
/// <see cref="AdminNotificationHub"/> is gated by the admin authorization policy, so every
/// connection on it already belongs to someone entitled to the volunteer list. The signal carries
/// no volunteer details; clients re-read the list over their own authorized page.
/// </summary>
public sealed class SignalRVolunteerRosterNotifier(
    IHubContext<AdminNotificationHub> hubContext,
    ILogger<SignalRVolunteerRosterNotifier> logger) : IVolunteerRosterNotifier
{
    public async Task NotifyRosterChangedAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

        try
        {
            await hubContext.Clients
                .All
                .SendCoreAsync("VolunteerRosterChanged", Array.Empty<object>(), cancellationToken);
        }
        catch (Exception exception)
        {
            // The roster change is already committed. A transport failure must not undo it; the
            // list is correct again on its next load.
            logger.LogWarning(exception, "Could not signal administrators that the volunteer roster changed.");
        }
    }
}
