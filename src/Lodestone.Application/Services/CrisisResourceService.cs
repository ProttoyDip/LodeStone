using Lodestone.Application.DTOs.Crisis;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Services;

public class CrisisResourceService : ICrisisResourceService
{
    /// <summary>Longest text the retriever will rank against. Longer input is ranked on its first part.</summary>
    public const int MaximumQueryLength = 500;

    private readonly ICrisisResourceRepository _repository;

    public CrisisResourceService(ICrisisResourceRepository repository) => _repository = repository;

    public Task<IReadOnlyList<CrisisResource>> GetActiveResourcesAsync(CancellationToken cancellationToken = default)
        => _repository.GetActiveOrderedAsync(cancellationToken);

    public async Task<IReadOnlyList<CrisisResourceMatch>> FindResourcesAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return Array.Empty<CrisisResourceMatch>();
        if (trimmed.Length > MaximumQueryLength) trimmed = trimmed[..MaximumQueryLength];

        var resources = await _repository.GetActiveOrderedAsync(cancellationToken);
        return CrisisResourceRetriever.Rank(trimmed, resources);
    }
}
