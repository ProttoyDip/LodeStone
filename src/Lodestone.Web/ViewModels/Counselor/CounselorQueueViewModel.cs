using Lodestone.Application.DTOs.Risk;
using Lodestone.Web.ViewModels.Risk;

namespace Lodestone.Web.ViewModels.Counselor;

public sealed class CounselorQueueViewModel
{
    public IReadOnlyList<RiskQueueItemDto> Items { get; init; } = Array.Empty<RiskQueueItemDto>();
    public DateTime RefreshedAtUtc { get; init; } = DateTime.UtcNow;
    public RiskRuntimeStatusViewModel? RiskRuntime { get; init; }
    public bool LoadFailed { get; init; }
    public string? ErrorMessage { get; init; }
}
