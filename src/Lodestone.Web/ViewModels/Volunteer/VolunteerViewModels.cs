using System.ComponentModel.DataAnnotations;
using Lodestone.Application.DTOs.Volunteer;

namespace Lodestone.Web.ViewModels.Volunteer;

public sealed class VolunteerDashboardViewModel
{
    public required VolunteerDashboardDto Dashboard { get; init; }
}

public sealed class VolunteerViewRequestViewModel
{
    public required SupportRequestDto Request { get; init; }
    public VolunteerInteractionInputModel Interaction { get; init; } = new();
    public VolunteerEscalationInputModel Escalation { get; init; } = new();
}

public sealed class VolunteerInteractionInputModel
{
    [Range(1, int.MaxValue)]
    public int RequestId { get; set; }

    [Required(ErrorMessage = "Enter a guidance message.")]
    [StringLength(2000, ErrorMessage = "The message must not exceed 2,000 characters.")]
    public string Message { get; set; } = string.Empty;
}

public sealed class VolunteerEscalationInputModel
{
    [Range(1, int.MaxValue)]
    public int RequestId { get; set; }

    [StringLength(2000, ErrorMessage = "The escalation note must not exceed 2,000 characters.")]
    public string? Message { get; set; }
}
