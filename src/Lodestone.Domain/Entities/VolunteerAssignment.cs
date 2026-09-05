using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

public class VolunteerAssignment : AuditableEntity
{
    public int VolunteerProfileId { get; set; }
    public VolunteerProfile? VolunteerProfile { get; set; }

    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public string Role { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
