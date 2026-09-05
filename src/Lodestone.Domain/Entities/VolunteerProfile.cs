using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

public class VolunteerProfile : AuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public string? Bio { get; set; }
    public string? Department { get; set; }
    public string? Skills { get; set; }
    public string? Availability { get; set; }
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<VolunteerAssignment> VolunteerAssignments { get; set; } = new List<VolunteerAssignment>();
    public ICollection<SupportRequest> SupportRequests { get; set; } = new List<SupportRequest>();
}
