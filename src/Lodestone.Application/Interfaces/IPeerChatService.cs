using Lodestone.Application.DTOs.Volunteer;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Server-owned membership for live support conversations.
/// </summary>
/// <remarks>
/// Both methods take the caller's user id explicitly rather than reading an ambient principal,
/// because they are called from a SignalR hub as well as from controllers, and a hub invocation
/// does not reliably carry an HTTP context.
/// </remarks>
public interface IPeerChatService
{
    /// <summary>
    /// The caller's room for a request, or <c>null</c> when they are neither the student who raised
    /// it nor the volunteer who holds it. Null is deliberately indistinguishable from "no such
    /// request" so probing ids reveals nothing.
    /// </summary>
    Task<PeerChatRoomDto?> ResolveRoomAsync(string userId, int requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a message from a participant. Throws <see cref="UnauthorizedAccessException"/> for a
    /// non-participant and <see cref="InvalidOperationException"/> when the request is not open.
    /// </summary>
    Task<PeerChatMessageDto> PostMessageAsync(string userId, int requestId, string message, CancellationToken cancellationToken = default);
}
