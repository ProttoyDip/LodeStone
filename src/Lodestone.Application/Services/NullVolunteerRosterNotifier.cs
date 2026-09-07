using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

/// <summary>Safe default used when no realtime transport is installed.</summary>
public sealed class NullVolunteerRosterNotifier : IVolunteerRosterNotifier
{
    public Task NotifyRosterChangedAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
