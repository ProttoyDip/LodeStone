using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

/// <summary>A supportive prompt sent to a student when disengagement is detected.</summary>
public class Nudge : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public string Message { get; set; } = string.Empty;
    public NudgeStatus Status { get; set; } = NudgeStatus.Pending;
    /// <summary>UTC time at which the prompt becomes visible to the student.</summary>
    public DateTime AvailableAtUtc { get; set; }
    /// <summary>UTC time after which the prompt is no longer actionable.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    /// <summary>True only for a counselor-created neutral support prompt.</summary>
    public bool IsManualCounselorNudge { get; set; }
}
