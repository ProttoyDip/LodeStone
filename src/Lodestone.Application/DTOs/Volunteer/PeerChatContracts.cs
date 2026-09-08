namespace Lodestone.Application.DTOs.Volunteer;

/// <summary>
/// A user's standing in one support conversation, resolved by the server from the request record.
/// The client never supplies membership; it supplies a request id and receives this or nothing.
/// </summary>
/// <param name="CanSend">
/// True only while the request is accepted and both participants are present. A pending request
/// has nobody to talk to; a completed or escalated one is history.
/// </param>
public sealed record PeerChatRoomDto(
    int RequestId,
    string RoomName,
    bool IsVolunteer,
    bool CanSend);

/// <summary>A persisted conversation message as broadcast to the room.</summary>
public sealed record PeerChatMessageDto(
    int Id,
    int RequestId,
    string SenderUserId,
    bool IsFromVolunteer,
    string Message,
    DateTime CreatedAtUtc);
