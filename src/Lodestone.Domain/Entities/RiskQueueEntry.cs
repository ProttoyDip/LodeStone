using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

/// <summary>An at-risk student surfaced to counselors for review/triage.</summary>
public class RiskQueueEntry : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public int RiskScoreId { get; set; }
    public RiskScore? RiskScore { get; set; }

    /// <summary>The score that originally opened this case. This never changes.</summary>
    public int TriggerRiskScoreId { get; set; }
    public RiskScore? TriggerRiskScore { get; set; }

    /// <summary>The highest level observed while this case has remained open.</summary>
    public RiskLevel Level { get; set; }
    public DateTime LastSignaledAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
