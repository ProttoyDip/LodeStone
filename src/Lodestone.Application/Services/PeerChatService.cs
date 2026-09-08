using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>
/// Resolves who may take part in a support conversation and records what they say.
/// </summary>
/// <remarks>
/// <para>
/// Membership is a property of the <see cref="SupportRequest"/> row: the student who raised it and
/// the volunteer who accepted it. Nothing the client sends can widen that set, which is the
/// precondition the governance document set for mapping a live chat hub at all.
/// </para>
/// <para>
/// Messages are stored as <see cref="SupportInteraction"/> rows, so the conversation a volunteer
/// can escalate to a counselor is the same conversation that happened live. No automated content
/// moderation is applied: a human volunteer is in the room, and escalation is theirs to decide.
/// </para>
/// </remarks>
public sealed class PeerChatService : IPeerChatService
{
    public const int MaximumMessageLength = 2000;

    private readonly IVolunteerSupportRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLog;
    private readonly TimeProvider _clock;

    public PeerChatService(
        IVolunteerSupportRepository repository,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLog,
        TimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _auditLog = auditLog;
        _clock = clock;
    }

    public static string RoomName(int requestId) => $"support-request-{requestId}";

    public async Task<PeerChatRoomDto?> ResolveRoomAsync(
        string userId, int requestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || requestId <= 0) return null;

        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        var role = Membership(request, userId);
        if (role is null) return null;

        return new PeerChatRoomDto(
            requestId,
            RoomName(requestId),
            IsVolunteer: role == Participant.Volunteer,
            CanSend: IsOpen(request!));
    }

    public async Task<PeerChatMessageDto> PostMessageAsync(
        string userId, int requestId, string message, CancellationToken cancellationToken = default)
    {
        var normalized = message?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > MaximumMessageLength)
            throw new ArgumentException($"Messages must be between 1 and {MaximumMessageLength} characters.", nameof(message));
        if (normalized.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("Messages cannot contain control characters.", nameof(message));

        var request = requestId > 0
            ? await _repository.GetRequestByIdAsync(requestId, cancellationToken)
            : null;
        var role = Membership(request, userId)
            ?? throw new UnauthorizedAccessException("You are not a participant in this conversation.");
        if (request is null || !IsOpen(request))
            throw new InvalidOperationException("This conversation is not open for new messages.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var interaction = new SupportInteraction
        {
            SupportRequestId = request.Id,
            // The volunteer column doubles as the "who spoke" marker the existing views read.
            VolunteerUserId = role == Participant.Volunteer ? userId : null,
            StudentUserId = request.StudentProfile?.UserId,
            Type = SupportInteractionType.Message,
            Message = normalized,
            CreatedAtUtc = nowUtc
        };

        await _repository.AddInteractionAsync(interaction, cancellationToken);
        request.ModifiedAtUtc = nowUtc;
        if (role == Participant.Volunteer) request.VolunteerLastReadAtUtc = nowUtc;
        else request.StudentLastReadAtUtc = nowUtc;
        _auditLog.Record(
            "SupportInteraction.Create",
            nameof(SupportInteraction),
            details: $"{(role == Participant.Volunteer ? "Volunteer" : "Student")} sent a live message on request {request.Id}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PeerChatMessageDto(
            interaction.Id,
            request.Id,
            userId,
            IsFromVolunteer: role == Participant.Volunteer,
            normalized,
            nowUtc);
    }

    private enum Participant { Student, Volunteer }

    private static Participant? Membership(SupportRequest? request, string userId)
    {
        if (request is null) return null;

        if (request.StudentProfile?.UserId == userId)
            return Participant.Student;

        if (request.VolunteerProfileId.HasValue
            && request.VolunteerProfile is { IsApproved: true, IsActive: true } volunteer
            && volunteer.UserId == userId
            && volunteer.User?.IsActive != false)
            return Participant.Volunteer;

        return null;
    }

    private static bool IsOpen(SupportRequest request)
        => request.Status == SupportRequestStatus.Accepted && request.VolunteerProfileId.HasValue;
}
