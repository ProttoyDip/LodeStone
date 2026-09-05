using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

public class StudentProfile : AuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// The administrator-verified LMS identifier. Student-submitted values remain
    /// in <see cref="StudentNumberClaims"/> until an administrator approves them.
    /// </summary>
    public string? StudentNumber { get; set; }
    public string? Program { get; set; }
    public int EnrollmentYear { get; set; }

    public ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();
    public ICollection<RiskFeatureSnapshot> RiskFeatureSnapshots { get; set; } = new List<RiskFeatureSnapshot>();
    public ICollection<RiskScore> RiskScores { get; set; } = new List<RiskScore>();
    public ICollection<RiskQueueEntry> RiskQueueEntries { get; set; } = new List<RiskQueueEntry>();
    public RiskMonitoringConsent? RiskMonitoringConsent { get; set; }
    public ICollection<RiskMonitoringConsentHistory> RiskMonitoringConsentHistory { get; set; } = new List<RiskMonitoringConsentHistory>();
    public ICollection<StudentNumberClaim> StudentNumberClaims { get; set; } = new List<StudentNumberClaim>();
    public StudentNudgePreference? NudgePreference { get; set; }
    public ICollection<Nudge> Nudges { get; set; } = new List<Nudge>();
    public ICollection<VolunteerAssignment> VolunteerAssignments { get; set; } = new List<VolunteerAssignment>();
    public ICollection<SupportRequest> SupportRequests { get; set; } = new List<SupportRequest>();
}
