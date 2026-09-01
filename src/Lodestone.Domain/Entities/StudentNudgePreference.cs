using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

/// <summary>
/// A student's independent preference for in-app support prompts. This does not
/// alter risk-monitoring consent or prevent a counselor from providing care.
/// </summary>
public class StudentNudgePreference : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    /// <summary>
    /// Separate, explicit opt-in for counselor-authored in-app prompts. A missing preference is
    /// treated as disabled so a student is never opted in by implication.
    /// </summary>
    public bool IsInAppNudgesEnabled { get; set; }
}
