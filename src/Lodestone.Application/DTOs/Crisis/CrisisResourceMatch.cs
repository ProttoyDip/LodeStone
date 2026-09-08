using Lodestone.Domain.Entities;

namespace Lodestone.Application.DTOs.Crisis;

/// <summary>
/// One resource the retriever found for what a person wrote, with the words in the resource that
/// matched. The resource is returned verbatim; nothing about it is generated or paraphrased.
/// </summary>
/// <remarks>
/// There is deliberately no category, label or assessment here. The retriever finds resources;
/// it does not decide what the person is going through.
/// </remarks>
public sealed record CrisisResourceMatch(
    CrisisResource Resource,
    double Score,
    IReadOnlyList<string> MatchedTerms);
