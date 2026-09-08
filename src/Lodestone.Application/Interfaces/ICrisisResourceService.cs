using Lodestone.Application.DTOs.Crisis;

namespace Lodestone.Application.Interfaces;

public interface ICrisisResourceService
{
    Task<IReadOnlyList<Domain.Entities.CrisisResource>> GetActiveResourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Active resources ranked against what a person wrote, using <c>CrisisResourceRetriever</c>.
    /// The text is used for ranking and discarded; it is never persisted or logged.
    /// </summary>
    Task<IReadOnlyList<CrisisResourceMatch>> FindResourcesAsync(string? query, CancellationToken cancellationToken = default);
}
