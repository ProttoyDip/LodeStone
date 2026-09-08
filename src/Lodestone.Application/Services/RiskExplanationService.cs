using System.Globalization;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public sealed class RiskExplanationService : IRiskExplanationService
{
    /// <summary>Below this many students a median is too noisy to be an honest comparison.</summary>
    public const int MinimumBaselinePopulation = 5;

    private readonly ICounselorQueueRepository _queue;
    private readonly IRiskModelExplainer _explainer;
    private readonly TimeProvider _clock;

    public RiskExplanationService(
        ICounselorQueueRepository queue,
        IRiskModelExplainer explainer,
        TimeProvider clock)
    {
        _queue = queue;
        _explainer = explainer;
        _clock = clock;
    }

    public async Task<RiskQueueExplanationDto?> ExplainQueueEntryAsync(
        int queueEntryId, CancellationToken cancellationToken = default)
    {
        if (queueEntryId <= 0) return null;

        var context = await _queue.GetExplanationContextAsync(
            queueEntryId,
            _clock.GetUtcNow().UtcDateTime,
            RiskScoringPolicy.MaximumSnapshotAgeDays,
            cancellationToken);
        if (context is null) return null;

        if (!_explainer.IsAvailable)
        {
            return new RiskQueueExplanationDto(
                context.QueueItem,
                null,
                "No risk model is loaded, so the score cannot be broken down. The score itself was produced by an earlier model run.");
        }

        if (context.Population.Count < MinimumBaselinePopulation)
        {
            return new RiskQueueExplanationDto(
                context.QueueItem,
                null,
                $"Fewer than {MinimumBaselinePopulation} consenting students share this feature schema, which is too few for an honest comparison group.");
        }

        RiskModelInput input;
        IReadOnlyList<RiskModelInput> population;
        try
        {
            input = RiskSnapshotModelInput.From(context.Snapshot);
            population = context.Population.Select(RiskSnapshotModelInput.From).ToArray();
        }
        catch (InvalidOperationException)
        {
            return new RiskQueueExplanationDto(
                context.QueueItem, null, "The stored snapshot is incomplete for its feature schema.");
        }

        var baseline = RiskExplanationBaseline.FromPopulation(
            context.Snapshot.FeatureSchemaVersion,
            population,
            $"the median of the most recent {context.Snapshot.ObservedDays}-day snapshot for each of " +
            $"{population.Count.ToString("N0", CultureInfo.InvariantCulture)} consenting students");

        RiskExplanation? explanation;
        try
        {
            explanation = _explainer.Explain(input, baseline);
        }
        catch (InvalidOperationException)
        {
            explanation = null;
        }

        return explanation is null
            ? new RiskQueueExplanationDto(
                context.QueueItem,
                null,
                "The loaded model uses a different feature schema from the one this score was computed with, so it cannot explain this score.")
            : new RiskQueueExplanationDto(context.QueueItem, explanation, null);
    }
}
