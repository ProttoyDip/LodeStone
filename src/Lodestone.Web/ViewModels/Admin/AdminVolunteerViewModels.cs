using System.ComponentModel.DataAnnotations;
using Lodestone.Application.DTOs.Volunteer;

namespace Lodestone.Web.ViewModels.Admin;

public sealed class AdminVolunteerIndexViewModel
{
    public required AdminVolunteerOverviewDto Overview { get; init; }
    public string? Query { get; init; }
}

public sealed class AdminVolunteerAssignmentViewModel
{
    public required VolunteerAssignmentOptionsDto Options { get; init; }
    public required VolunteerAssignmentInputModel Input { get; init; }
}

public sealed class VolunteerAssignmentInputModel : IValidatableObject
{
    [Range(1, int.MaxValue)]
    public int VolunteerProfileId { get; set; }

    [Required(ErrorMessage = "Choose an assignment target.")]
    public VolunteerAssignmentTarget? Target { get; set; }

    public int? StudentProfileId { get; set; }

    [StringLength(200)]
    public string? Program { get; set; }

    [Range(1900, 2200, ErrorMessage = "Choose a valid enrollment year.")]
    public int? EnrollmentYear { get; set; }

    [Required(ErrorMessage = "Enter the volunteer's role.")]
    [StringLength(100)]
    public string Role { get; set; } = "Peer Mentor";

    [StringLength(500)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Target == VolunteerAssignmentTarget.Student && (!StudentProfileId.HasValue || StudentProfileId <= 0))
        {
            yield return new ValidationResult(
                "Choose a student.",
                new[] { nameof(StudentProfileId) });
        }

        if (Target == VolunteerAssignmentTarget.Group)
        {
            if (string.IsNullOrWhiteSpace(Program))
            {
                yield return new ValidationResult(
                    "Choose a student group.",
                    new[] { nameof(Program) });
            }

            if (!EnrollmentYear.HasValue)
            {
                yield return new ValidationResult(
                    "Choose a student group.",
                    new[] { nameof(EnrollmentYear) });
            }
        }
    }
}
