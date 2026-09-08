using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Hubs;

/// <summary>
/// Live messaging inside one support conversation.
/// </summary>
/// <remarks>
/// <para>
/// The client never names a room. It names a support request, and <see cref="IPeerChatService"/>
/// decides whether the connected user is one of that request's two participants. Group membership
/// is derived from that answer and re-checked on every send, so a stale connection cannot keep
/// speaking into a conversation it has since left.
/// </para>
/// <para>
/// Every message is persisted before it is broadcast. Nothing here is transient, and nothing here
/// is moderated by a machine: the volunteer in the room can escalate to a counselor.
/// </para>
/// </remarks>
[Authorize]
public class PeerChatHub : Hub
{
    public const string Route = "/hubs/peer-chat";

    private readonly IPeerChatService _chat;

    public PeerChatHub(IPeerChatService chat) => _chat = chat;

    public async Task<PeerChatRoomDto> JoinRequest(int requestId)
    {
        var room = await _chat.ResolveRoomAsync(RequireUserId(), requestId, Context.ConnectionAborted)
            ?? throw new HubException("You are not a participant in this conversation.");

        await Groups.AddToGroupAsync(Context.ConnectionId, room.RoomName, Context.ConnectionAborted);
        return room;
    }

    public async Task<PeerChatMessageDto> SendMessage(int requestId, string message)
    {
        PeerChatMessageDto saved;
        try
        {
            saved = await _chat.PostMessageAsync(RequireUserId(), requestId, message, Context.ConnectionAborted);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new HubException(exception.Message);
        }

        await Clients.Group(PeerChatService.RoomName(requestId)).SendAsync("ReceiveMessage", saved, Context.ConnectionAborted);
        return saved;
    }

    private string RequireUserId()
        => Context.UserIdentifier ?? throw new HubException("You must be signed in.");
}
