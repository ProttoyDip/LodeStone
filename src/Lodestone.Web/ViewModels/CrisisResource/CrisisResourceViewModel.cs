using Lodestone.Application.DTOs.Crisis;
using CrisisResourceEntity = Lodestone.Domain.Entities.CrisisResource;

namespace Lodestone.Web.ViewModels.CrisisResource;

public class CrisisResourceViewModel
{
    public IReadOnlyList<CrisisResourceEntity> EmergencyResources { get; init; } = Array.Empty<CrisisResourceEntity>();
    public IReadOnlyList<CrisisResourceEntity> SupportResources { get; init; } = Array.Empty<CrisisResourceEntity>();

    /// <summary>What the person typed, echoed back into their own search box only.</summary>
    public string? Query { get; init; }

    /// <summary>True when a search was submitted, so the page can say "nothing matched" honestly.</summary>
    public bool Searched { get; init; }

    public IReadOnlyList<CrisisResourceMatch> Matches { get; init; } = Array.Empty<CrisisResourceMatch>();
}
