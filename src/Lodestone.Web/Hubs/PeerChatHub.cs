using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Hubs;

/// <summary>Real-time peer-support chat rooms.</summary>
[Authorize]
public class PeerChatHub : Hub
{
    public const string Route = "/hubs/peer-chat";
    private const int MaximumRoomLength = 64;
    private const int MaximumMessageLength = 1_000;

    public async Task SendMessage(string room, string message)
    {
        var validatedRoom = ValidateRoom(room);
        var validatedMessage = ValidateMessage(message);
        await Clients.Group(validatedRoom)
            .SendAsync("ReceiveMessage", Context.UserIdentifier, validatedMessage);
    }

    public async Task JoinRoom(string room)
        => await Groups.AddToGroupAsync(Context.ConnectionId, ValidateRoom(room));

    private static string ValidateRoom(string? room)
    {
        var candidate = room?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > MaximumRoomLength ||
            candidate.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new HubException(
                "Room names must be 1-64 characters using only letters, numbers, '-' or '_'.");
        }

        return candidate;
    }

    private static string ValidateMessage(string? message)
    {
        var candidate = message?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaximumMessageLength)
        {
            throw new HubException("Messages must be between 1 and 1,000 characters.");
        }

        if (candidate.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            throw new HubException("Messages cannot contain control characters.");
        }

        return candidate;
    }
}
