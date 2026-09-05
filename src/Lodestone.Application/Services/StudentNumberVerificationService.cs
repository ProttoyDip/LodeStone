using System.Text.RegularExpressions;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>Validates LMS identifiers and coordinates student/admin verification operations.</summary>
public sealed partial class StudentNumberVerificationService : IStudentNumberVerificationService
{
    private const int MaximumStudentNumberLength = 64;
    private const int MaximumActorLength = 450;
    private readonly IStudentNumberVerificationRepository _repository;
    private readonly INotificationService _notifications;

    public StudentNumberVerificationService(
        IStudentNumberVerificationRepository repository,
        INotificationService notifications)
    {
        _repository = repository;
        _notifications = notifications;
    }

    public Task<StudentNumberVerificationStateDto?> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<StudentNumberVerificationStateDto?>(null);

        return _repository.GetCurrentByUserIdAsync(userId.Trim(), cancellationToken);
    }

    public async Task<StudentNumberClaimResultDto> SubmitAsync(
        string userId,
        string studentNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.NotFound);
        if (!TryNormalize(studentNumber, out var normalized))
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.InvalidStudentNumber);

        var result = await _repository.SubmitAsync(userId.Trim(), normalized, cancellationToken);

        // A newly submitted claim sits in the admin review queue, so raise it for whoever is on
        // duty. Only a genuine submission notifies: re-submissions and validation rejections must
        // not create queue noise. The student number itself stays out of the message body.
        if (result.Outcome == StudentNumberClaimOutcome.Submitted)
        {
            await _notifications.NotifyAdministratorsAsync(
                NotificationType.System,
                "Student number awaiting review",
                "A student submitted a student number claim for verification.",
                cancellationToken);
        }

        return result;
    }

    public Task<IReadOnlyList<StudentNumberClaimDto>> GetPendingAsync(
        CancellationToken cancellationToken = default)
        => _repository.GetPendingAsync(cancellationToken);

    public Task<IReadOnlyList<VerifiedStudentNumberDto>> GetVerifiedAsync(
        CancellationToken cancellationToken = default)
        => _repository.GetVerifiedAsync(cancellationToken);

    public Task<StudentNumberClaimResultDto> ApproveAsync(
        int claimId,
        string reviewerUserId,
        string rowVersionToken,
        CancellationToken cancellationToken = default)
        => ReviewAsync(claimId, true, reviewerUserId, rowVersionToken, cancellationToken);

    public Task<StudentNumberClaimResultDto> RejectAsync(
        int claimId,
        string reviewerUserId,
        string rowVersionToken,
        CancellationToken cancellationToken = default)
        => ReviewAsync(claimId, false, reviewerUserId, rowVersionToken, cancellationToken);

    public Task<StudentNumberClaimResultDto> ResetAsync(
        int studentProfileId,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (studentProfileId <= 0 || !TryNormalizeActor(reviewerUserId, out var actor))
            return Task.FromResult(new StudentNumberClaimResultDto(StudentNumberClaimOutcome.InvalidRequest));

        return _repository.ResetAsync(studentProfileId, actor, cancellationToken);
    }

    private Task<StudentNumberClaimResultDto> ReviewAsync(
        int claimId,
        bool approve,
        string reviewerUserId,
        string rowVersionToken,
        CancellationToken cancellationToken)
    {
        if (claimId <= 0 || !TryNormalizeActor(reviewerUserId, out var actor))
            return Task.FromResult(new StudentNumberClaimResultDto(StudentNumberClaimOutcome.InvalidRequest));
        if (!TryDecodeRowVersion(rowVersionToken, out var expectedRowVersion))
            return Task.FromResult(new StudentNumberClaimResultDto(StudentNumberClaimOutcome.ConcurrencyConflict));

        return _repository.ReviewAsync(
            claimId,
            approve,
            actor,
            expectedRowVersion,
            cancellationToken);
    }

    internal static bool TryNormalize(string? studentNumber, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(studentNumber)
            ? string.Empty
            : studentNumber.Trim().ToUpperInvariant();
        return normalized.Length is > 0 and <= MaximumStudentNumberLength &&
               StudentNumberPattern().IsMatch(normalized);
    }

    private static bool TryNormalizeActor(string? actorUserId, out string actor)
    {
        actor = string.IsNullOrWhiteSpace(actorUserId) ? string.Empty : actorUserId.Trim();
        if (actor.Length > MaximumActorLength) actor = actor[..MaximumActorLength];
        return actor.Length > 0;
    }

    private static bool TryDecodeRowVersion(string? token, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            rowVersion = Convert.FromBase64String(token);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StudentNumberPattern();
}
