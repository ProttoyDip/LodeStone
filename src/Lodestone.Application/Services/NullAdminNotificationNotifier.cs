using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

/// <summary>Safe default used when no realtime transport is installed.</summary>
public sealed class NullAdminNotificationNotifier : IAdminNotificationNotifier
{
    public Task NotifyChangedAsync(string recipientUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
