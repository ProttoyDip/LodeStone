using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

public class SupportRequest : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public int? VolunteerProfileId { get; set; }
    public VolunteerProfile? VolunteerProfile { get; set; }

    public SupportRequestCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Availability { get; set; }
    public SupportRequestStatus Status { get; set; } = SupportRequestStatus.Pending;
    public bool IsVisibleToVolunteers { get; set; } = true;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? EscalatedAtUtc { get; set; }

    /// <summary>When each participant last opened the conversation; drives unread indicators.</summary>
    public DateTime? StudentLastReadAtUtc { get; set; }
    public DateTime? VolunteerLastReadAtUtc { get; set; }

    /// <summary>Set when a counselor takes ownership of an escalation, closing the handoff loop.</summary>
    public DateTime? EscalationHandledAtUtc { get; set; }
    public string? EscalationHandledByUserId { get; set; }
    public string? EscalationHandledNote { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<SupportInteraction> Interactions { get; set; } = new List<SupportInteraction>();
}
