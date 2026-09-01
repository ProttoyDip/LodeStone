using System.ComponentModel.DataAnnotations;
using Lodestone.Domain.Enums;

namespace Lodestone.Web.ViewModels.Student;

public class RequestSupportViewModel
{
    [Required(ErrorMessage = "Select a category.")]
    public SupportRequestCategory? Category { get; set; }

    [StringLength(2000, ErrorMessage = "Message must not exceed 2,000 characters.")]
    public string? Message { get; set; }

    [StringLength(500, ErrorMessage = "Availability must not exceed 500 characters.")]
    public string? Availability { get; set; }
}

public sealed class StudentSupportRequestsViewModel
{
    public IReadOnlyList<Application.DTOs.Volunteer.SupportRequestDto> Pending { get; init; }
        = Array.Empty<Application.DTOs.Volunteer.SupportRequestDto>();
    public IReadOnlyList<Application.DTOs.Volunteer.SupportRequestDto> Active { get; init; }
        = Array.Empty<Application.DTOs.Volunteer.SupportRequestDto>();
    public IReadOnlyList<Application.DTOs.Volunteer.SupportRequestDto> History { get; init; }
        = Array.Empty<Application.DTOs.Volunteer.SupportRequestDto>();
}
