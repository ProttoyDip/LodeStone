namespace Lodestone.Application.Interfaces;

/// <summary>
/// Framework-neutral notification boundary for refreshing peer-support participants.
/// </summary>
/// <remarks>
/// The signal is addressed to named users and carries no payload: it says only that something the
/// recipient can already see may have changed, and the client re-reads its own data over an
/// authorized endpoint. That is what makes this safe where a broadcast would not be — a support
/// request is a private conversation between one student and one volunteer, and pushing its
/// content to a room would risk delivering it to someone entitled to none of it.
/// </remarks>
public interface IPeerSupportNotifier
{
    Task NotifyChangedAsync(
        IReadOnlyCollection<string> recipientUserIds,
        CancellationToken cancellationToken = default);
}
