using Microsoft.AspNetCore.Identity;

namespace Lodestone.Domain.Entities;

/// <summary>Identity user. Concrete role data lives in the profile entities below.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    /// <summary>Last time the account made a request or sent a heartbeat; drives the admin online/offline view.</summary>
    public DateTime? LastSeenUtc { get; set; }
    /// <summary>IANA time zone the user's browser reported (for example "Asia/Dhaka"); used for emails and drafts.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? TimeZoneId { get; set; }
    public bool IsActive { get; set; } = true;

    public StudentProfile? StudentProfile { get; set; }
    public CounselorProfile? CounselorProfile { get; set; }
    public VolunteerProfile? VolunteerProfile { get; set; }
}
