using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Hubs;

/// <summary>
/// Refresh signals for peer-support participants.
/// </summary>
/// <remarks>
/// This hub has no groups and no client-callable methods, which is what makes it safe to map where
/// PeerChatHub is not. Membership of a support conversation is never asserted by the client and
/// never held in the hub: the server resolves who may see a request and addresses each of them
/// individually, and the signal carries no content for a wrong recipient to read.
/// <para>
/// Any authenticated user may connect; connecting reveals nothing, because a connection only ever
/// receives signals the server chose to send to that specific user.
/// </para>
/// </remarks>
[Authorize]
public class PeerSupportHub : Hub
{
    public const string Route = "/hubs/peer-support";
}
