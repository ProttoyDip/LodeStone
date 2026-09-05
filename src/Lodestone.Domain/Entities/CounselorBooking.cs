using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

public class CounselorBooking : AuditableEntity
{
    public int CounselorProfileId { get; set; }
    public CounselorProfile? CounselorProfile { get; set; }

    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public int? AvailabilitySlotId { get; set; }
    public CounselorAvailabilitySlot? AvailabilitySlot { get; set; }

    public DateTime ScheduledForUtc { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Requested;
    public string? Notes { get; set; }

    /// <summary>
    /// When the upcoming-session reminder was sent, or null if it has not been. Recorded so a
    /// repeated sweep cannot email the same student about the same session twice.
    /// </summary>
    public DateTime? ReminderSentAtUtc { get; set; }

    public CounselorSessionReport? SessionReport { get; set; }
}
