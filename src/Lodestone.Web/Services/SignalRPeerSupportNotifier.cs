using Lodestone.Application.Interfaces;
using Lodestone.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Services;

/// <summary>
/// Sends a payload-free refresh signal to named peer-support participants.
/// </summary>
/// <remarks>
/// Each recipient is addressed individually through Clients.User, which resolves via the default
/// IUserIdProvider (the NameIdentifier claim). Nothing about the request travels over the hub, so a
/// connection that should not see a conversation cannot learn anything from the signal.
/// </remarks>
public sealed class SignalRPeerSupportNotifier(
    IHubContext<PeerSupportHub> hubContext,
    ILogger<SignalRPeerSupportNotifier> logger) : IPeerSupportNotifier
{
    public async Task NotifyChangedAsync(
        IReadOnlyCollection<string> recipientUserIds,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || recipientUserIds.Count == 0) return;

        try
        {
            await hubContext.Clients
                .Users(recipientUserIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray())
                .SendCoreAsync("PeerSupportChanged", Array.Empty<object>(), cancellationToken);
        }
        catch (Exception exception)
        {
            // The underlying change has already been persisted; only the nudge failed, and the
            // page shows the new state on its next load regardless.
            logger.LogWarning(exception, "Could not signal peer-support participants.");
        }
    }
}
