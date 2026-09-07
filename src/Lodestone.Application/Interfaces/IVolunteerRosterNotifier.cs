namespace Lodestone.Application.Interfaces;

/// <summary>
/// Framework-neutral boundary for telling administrators that the volunteer roster changed.
/// The signal is payload-free and carries no volunteer's details: it only says "the list you are
/// looking at is out of date", and the client re-reads the list over its own authorized page.
/// Implementations address administrators as a group, so there is no recipient to resolve and
/// nothing recipient-scoped to leak.
/// </summary>
public interface IVolunteerRosterNotifier
{
    /// <summary>Signals administrators that the volunteer roster may have changed.</summary>
    Task NotifyRosterChangedAsync(CancellationToken cancellationToken = default);
}
