using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

/// <summary>An immutable record of a student's monitoring-consent transition.</summary>
public class RiskMonitoringConsentHistory : BaseEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public bool IsConsented { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string? ChangedByUserId { get; set; }
}
