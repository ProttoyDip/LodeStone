using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Web.ViewModels.Risk;

namespace Lodestone.Web.ViewModels.Counselor;

public sealed class CounselorQueueViewModel
{
    public IReadOnlyList<RiskQueueItemDto> Items { get; init; } = Array.Empty<RiskQueueItemDto>();
    public DateTime RefreshedAtUtc { get; init; } = DateTime.UtcNow;
    public RiskRuntimeStatusViewModel? RiskRuntime { get; init; }
    public bool LoadFailed { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Peer-support requests volunteers have handed over and no counselor has taken yet.</summary>
    public IReadOnlyList<PeerEscalationDto> PeerEscalations { get; init; } = Array.Empty<PeerEscalationDto>();
    public string? PeerEscalationsError { get; init; }
}
