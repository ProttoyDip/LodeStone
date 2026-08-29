namespace Lodestone.Application.Interfaces;

/// <summary>
/// Framework-neutral notification boundary for refreshing authorized counselor clients.
/// Implementations must not include student data in the notification payload.
/// </summary>
public interface IRiskQueueNotifier
{
    Task NotifyChangedAsync(CancellationToken cancellationToken = default);
}
