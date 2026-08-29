using System.ComponentModel.DataAnnotations;

namespace Lodestone.Web.ViewModels.Student;

public sealed class StudentNumberClaimViewModel
{
    [Required]
    [Display(Name = "Student number")]
    [StringLength(64)]
    [RegularExpression(
        @"^[A-Za-z0-9][A-Za-z0-9._/-]{0,63}$",
        ErrorMessage = "Use 1-64 letters, numbers, periods, underscores, slashes, or hyphens.")]
    public string StudentNumber { get; set; } = string.Empty;
}
