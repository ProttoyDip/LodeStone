using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

/// <summary>
/// A student's request to associate an LMS student number with their profile.
/// Only an approved claim may be copied to <see cref="StudentProfile.StudentNumber"/>.
/// </summary>
public class StudentNumberClaim : AuditableEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public string ClaimedStudentNumber { get; set; } = string.Empty;
    public StudentNumberClaimStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
