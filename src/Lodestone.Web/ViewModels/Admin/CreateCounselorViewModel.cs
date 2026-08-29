using System.ComponentModel.DataAnnotations;

namespace Lodestone.Web.ViewModels.Admin;

public sealed class CreateCounselorViewModel
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Specialization { get; set; }
}
