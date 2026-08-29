using System.ComponentModel.DataAnnotations;

namespace Lodestone.Web.ViewModels.Auth;

public class RegisterViewModel
{
    [Required, Display(Name = "Full name"), StringLength(120, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Student number"), StringLength(64)]
    [RegularExpression(
        @"^[A-Za-z0-9][A-Za-z0-9._/-]{0,63}$",
        ErrorMessage = "Use 1–64 letters, numbers, periods, underscores, slashes, or hyphens.")]
    public string StudentNumber { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8,
        ErrorMessage = "The {0} must be at least {2} characters long.")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Display(Name = "I agree to the privacy commitment")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the privacy commitment to continue.")]
    public bool AcceptPrivacy { get; set; }

    [Display(Name = "Enable weekly risk monitoring")]
    public bool EnableRiskMonitoring { get; set; }
}
