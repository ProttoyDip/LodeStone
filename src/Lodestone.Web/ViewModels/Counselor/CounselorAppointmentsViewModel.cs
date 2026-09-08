using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Nudges;

namespace Lodestone.Web.ViewModels.Counselor;

public sealed class CounselorAppointmentsViewModel
{
    public CounselorAppointmentsPageDto? Page { get; init; }
    public DateTime RefreshedAtUtc { get; init; }
    public bool LoadFailed { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Outcomes of prompts this counselor already sent, keyed by booking id.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<ManualNudgeOutcomeDto>> NudgeOutcomes { get; init; }
        = new Dictionary<int, IReadOnlyList<ManualNudgeOutcomeDto>>();
}
