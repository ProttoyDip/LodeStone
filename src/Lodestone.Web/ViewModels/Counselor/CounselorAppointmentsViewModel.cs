using Lodestone.Application.DTOs.Booking;

namespace Lodestone.Web.ViewModels.Counselor;

public sealed class CounselorAppointmentsViewModel
{
    public CounselorAppointmentsPageDto? Page { get; init; }
    public DateTime RefreshedAtUtc { get; init; }
    public bool LoadFailed { get; init; }
    public string? ErrorMessage { get; init; }
}
