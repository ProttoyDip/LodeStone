using Lodestone.Application.Interfaces;
using Lodestone.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Services;

/// <summary>Sends a payload-free refresh signal after a committed queue mutation.</summary>
public sealed class SignalRRiskQueueNotifier(
    IHubContext<CounselorQueueHub> hubContext,
    ILogger<SignalRRiskQueueNotifier> logger) : IRiskQueueNotifier
{
    public async Task NotifyChangedAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            await hubContext.Clients.All.SendCoreAsync(
                "QueueUpdated",
                Array.Empty<object>(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Persistence has already committed. A transient push failure must not
            // turn a successful scoring/resolve operation into a retry.
            logger.LogWarning(exception, "Could not notify counselor clients that the risk queue changed.");
        }
    }
}
