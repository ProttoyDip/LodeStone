using System.Globalization;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public sealed class RiskExplanationService : IRiskExplanationService
{
    /// <summary>Below this many students a median is too noisy to be an honest comparison.</summary>
    public const int MinimumBaselinePopulation = 5;

    /// <summary>Cap on population rows used for the fallback importance estimate; keeps the page fast.</summary>
    public const int MaximumImportanceSample = 150;

    private readonly ICounselorQueueRepository _queue;
    private readonly IRiskModelExplainer _explainer;
    private readonly IRiskModelPredictor _predictor;
    private readonly TimeProvider _clock;

    public RiskExplanationService(
        ICounselorQueueRepository queue,
        IRiskModelExplainer explainer,
        IRiskModelPredictor predictor,
        TimeProvider clock)
    {
        _queue = queue;
        _explainer = explainer;
        _predictor = predictor;
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

        if (explanation is null)
        {
            return new RiskQueueExplanationDto(
                context.QueueItem,
                null,
                "The loaded model uses a different feature schema from the one this score was computed with, so it cannot explain this score.");
        }

        var (importance, source) = ModelImportance(population, baseline);
        return new RiskQueueExplanationDto(context.QueueItem, explanation, null)
        {
            ModelImportance = importance,
            ModelImportanceSource = source
        };
    }

    /// <summary>
    /// Training-time permutation importance when the artifact recorded it; otherwise the mean
    /// absolute ablation effect across the current consenting population, using the same explainer
    /// that produced the per-student factors so the two views are directly comparable.
    /// </summary>
    private (IReadOnlyList<RiskFeatureImportance> Importance, string? Source) ModelImportance(
        IReadOnlyList<RiskModelInput> population,
        RiskExplanationBaseline baseline)
    {
        RiskModelDescriptor descriptor;
        try
        {
            descriptor = _predictor.Descriptor;
        }
        catch (InvalidOperationException)
        {
            return (Array.Empty<RiskFeatureImportance>(), null);
        }

        if (descriptor.FeatureImportance.Count > 0)
        {
            return (descriptor.FeatureImportance,
                "Measured when the model was trained: how much its accuracy fell when each measure was shuffled across the validation students.");
        }

        var sample = population.Count <= MaximumImportanceSample
            ? population
            : population.Take(MaximumImportanceSample).ToArray();
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        var explained = 0;
        foreach (var member in sample)
        {
            RiskExplanation? memberExplanation;
            try
            {
                memberExplanation = _explainer.Explain(member, baseline);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            if (memberExplanation is null) continue;
            explained++;
            foreach (var factor in memberExplanation.Factors)
                totals[factor.FeatureName] = totals.GetValueOrDefault(factor.FeatureName) + factor.Magnitude;
        }

        if (explained < MinimumBaselinePopulation || totals.Count == 0)
            return (Array.Empty<RiskFeatureImportance>(), null);

        var grandTotal = totals.Values.Sum();
        var ranked = totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select((pair, index) => new RiskFeatureImportance(
                pair.Key,
                index + 1,
                pair.Value / explained,
                grandTotal <= 0 ? 0d : pair.Value / grandTotal))
            .ToArray();

        return (ranked,
            $"Estimated now from {explained.ToString("N0", CultureInfo.InvariantCulture)} consenting students: the average size of each measure's effect on their own scores. The next trained model will record this at training time instead.");
    }
}
