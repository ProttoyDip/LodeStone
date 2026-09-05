using Lodestone.Application.DTOs.Nudges;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>
/// Handles optional, neutral in-app prompts. Risk-model output never creates a
/// nudge in this delivery; counselor-created prompts require a real booking
/// relationship and a separate student preference.
/// </summary>
public sealed class NudgeService : INudgeService
{
    private const int ManualNudgeCooldownDays = 7;
    private const int NudgeLifetimeDays = 14;
    private const int SnoozeDays = 7;

    private readonly INudgeRepository _nudges;
    private readonly IBookingRepository _bookings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _timeProvider;

    public NudgeService(
        INudgeRepository nudges,
        IBookingRepository bookings,
        IUnitOfWork unitOfWork,
        IAuditLogService audit,
        TimeProvider timeProvider)
    {
        _nudges = nudges;
        _bookings = bookings;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<StudentNudgeStateDto?> GetForStudentAsync(
        string studentUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentUserId)) return null;

        var student = await _nudges.GetStudentByUserIdAsync(studentUserId, cancellationToken);
        if (student is null) return null;

        var nowUtc = UtcNow;
        var enabled = student.NudgePreference?.IsInAppNudgesEnabled == true;
        var active = enabled
            ? await _nudges.GetActiveForStudentAsync(student.Id, nowUtc, cancellationToken)
            : Array.Empty<Nudge>();

        return new StudentNudgeStateDto(
            enabled,
            active.Select(nudge => new StudentNudgeDto(
                nudge.Id,
                nudge.Message,
                nudge.Status,
                nudge.AvailableAtUtc,
                nudge.ExpiresAtUtc,
                nudge.Status is NudgeStatus.Pending or NudgeStatus.Sent or NudgeStatus.Snoozed))
                .ToArray());
    }

    public async Task<NudgeMutationResult> SetInAppPreferenceAsync(
        string studentUserId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentUserId)) return NudgeMutationResult.NotFound;

        var student = await _nudges.GetStudentByUserIdAsync(studentUserId, cancellationToken);
        if (student is null) return NudgeMutationResult.NotFound;

        var nowUtc = UtcNow;
        if (student.NudgePreference is null)
        {
            student.NudgePreference = new StudentNudgePreference
            {
                StudentProfileId = student.Id,
                IsInAppNudgesEnabled = enabled,
                CreatedAtUtc = nowUtc,
                CreatedBy = studentUserId.Trim()
            };
        }
        else
        {
            student.NudgePreference.IsInAppNudgesEnabled = enabled;
            student.NudgePreference.ModifiedAtUtc = nowUtc;
            student.NudgePreference.ModifiedBy = studentUserId.Trim();
        }

        _audit.Record(
            enabled ? "NudgePreference.Enabled" : "NudgePreference.Disabled",
            nameof(StudentNudgePreference),
            student.Id.ToString(),
            "The student changed optional in-app support prompts.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return NudgeMutationResult.Updated;
    }

    public async Task<NudgeMutationResult> RespondAsync(
        string studentUserId,
        int nudgeId,
        NudgeResponseAction action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentUserId) || nudgeId <= 0)
            return NudgeMutationResult.InvalidRequest;

        var student = await _nudges.GetStudentByUserIdAsync(studentUserId, cancellationToken);
        if (student is null) return NudgeMutationResult.NotFound;

        var nowUtc = UtcNow;
        var nudge = await _nudges.GetActionableAsync(student.Id, nudgeId, nowUtc, cancellationToken);
        if (nudge is null) return NudgeMutationResult.NotActionable;

        switch (action)
        {
            case NudgeResponseAction.Acknowledge:
                nudge.Status = NudgeStatus.Acknowledged;
                nudge.AcknowledgedAtUtc = nowUtc;
                break;
            case NudgeResponseAction.Dismiss:
                nudge.Status = NudgeStatus.Dismissed;
                nudge.DismissedAtUtc = nowUtc;
                break;
            case NudgeResponseAction.Snooze:
                nudge.Status = NudgeStatus.Snoozed;
                nudge.SnoozedUntilUtc = nowUtc.AddDays(SnoozeDays);
                nudge.AvailableAtUtc = nudge.SnoozedUntilUtc.Value;
                break;
            default:
                return NudgeMutationResult.InvalidRequest;
        }

        nudge.ModifiedAtUtc = nowUtc;
        nudge.ModifiedBy = studentUserId.Trim();
        _audit.Record(
            $"Nudge.{action}",
            nameof(Nudge),
            nudge.Id.ToString(),
            "The student responded to an optional in-app support prompt.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return NudgeMutationResult.Updated;
    }

    public async Task<NudgeMutationResult> CreateManualForBookingAsync(
        string counselorUserId,
        int bookingId,
        ManualNudgeTemplate template,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(counselorUserId) || bookingId <= 0 || !Enum.IsDefined(template))
            return NudgeMutationResult.InvalidRequest;

        var counselor = await _bookings.GetCounselorByUserIdAsync(counselorUserId, cancellationToken);
        if (counselor is null) return NudgeMutationResult.NotFound;

        var booking = await _nudges.GetOwnedBookingAsync(counselor.Id, bookingId, cancellationToken);
        if (booking?.StudentProfile is null) return NudgeMutationResult.NotEligible;

        var preference = booking.StudentProfile.NudgePreference;
        if (preference?.IsInAppNudgesEnabled != true)
            return NudgeMutationResult.PreferenceDisabled;

        var nowUtc = UtcNow;
        if (await _nudges.HasManualNudgeSinceAsync(
                booking.StudentProfileId,
                nowUtc.AddDays(-ManualNudgeCooldownDays),
                cancellationToken))
        {
            return NudgeMutationResult.CooldownActive;
        }

        var nudge = new Nudge
        {
            StudentProfileId = booking.StudentProfileId,
            Message = TemplateMessage(template),
            Status = NudgeStatus.Pending,
            AvailableAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddDays(NudgeLifetimeDays),
            IsManualCounselorNudge = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = counselorUserId.Trim()
        };
        await _nudges.AddAsync(nudge, cancellationToken);
        _audit.Record(
            "Nudge.CreatedByCounselor",
            nameof(Nudge),
            null,
            $"Created for a student connected to booking {bookingId}; template {template}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return NudgeMutationResult.Updated;
    }

    /// <summary>
    /// Automatic model-based prompts remain deliberately disabled until separate
    /// product approval. This method exists for the Hangfire boundary and is a
    /// safe no-op rather than a hidden risk-based side effect.
    /// </summary>
    public Task GenerateNudgesForAtRiskStudentsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task DispatchPendingNudgesAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _nudges.GetPendingDispatchAsync(UtcNow, cancellationToken);
        if (pending.Count == 0) return;

        var nowUtc = UtcNow;
        foreach (var nudge in pending)
        {
            nudge.Status = NudgeStatus.Sent;
            nudge.SentAtUtc = nowUtc;
            nudge.ModifiedAtUtc = nowUtc;
            nudge.ModifiedBy = "system:nudge-dispatch";
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private static string TemplateMessage(ManualNudgeTemplate template)
        => template switch
        {
            ManualNudgeTemplate.CheckIn =>
                "A counselor is available if you would like to check in. You can choose what support feels useful.",
            ManualNudgeTemplate.BookingFollowUp =>
                "If you would like another conversation, you can review available counselor appointments whenever you are ready.",
            ManualNudgeTemplate.SupportResources =>
                "Support resources are available whenever you need them. You can explore options at your own pace.",
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
}
