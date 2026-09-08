using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

/// <summary>Safe default used when no realtime transport is installed.</summary>
public sealed class NullPeerSupportNotifier : IPeerSupportNotifier
{
    public Task NotifyChangedAsync(
        IReadOnlyCollection<string> recipientUserIds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
