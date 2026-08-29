using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

/// <summary>The student's current, reversible behavioral-monitoring choice.</summary>
public class RiskMonitoringConsent : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public bool IsConsented { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTime? ConsentedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
}
