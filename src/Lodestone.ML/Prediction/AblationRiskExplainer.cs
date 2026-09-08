using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.ML.Prediction;

/// <summary>
/// Explains a prediction by measuring what the model actually does when one feature is changed.
/// </summary>
/// <remarks>
/// <para>
/// For each feature the student's value is replaced by the baseline value, everything else is held
/// still, and the model is asked again. The difference between the real score and that score is
/// this feature's contribution. Nothing is approximated and no surrogate model is fitted: these are
/// the model's own outputs.
/// </para>
/// <para>
/// Chosen over ML.NET's feature-contribution transform deliberately. That transform requires
/// reaching into the trained predictor and casting it to a tree-specific interface, which would
/// silently stop working the day the winning candidate changes from LightGBM to something else --
/// exactly the kind of failure that surfaces as a blank panel in front of a counselor. Ablation
/// treats the model as a black box, so it survives any retraining, and costs one extra prediction
/// per feature: seventeen, for the current schema.
/// </para>
/// <para>
/// <b>What this is not.</b> One-at-a-time ablation attributes interactions to whichever feature is
/// varied, so contributions need not sum to the score the way Shapley values would. It answers
/// "what does this one input do to the output, on its own" -- which is the question a counselor is
/// actually asking -- and it makes no causal claim about the student.
/// </para>
/// </remarks>
public sealed class AblationRiskExplainer : IRiskModelExplainer
{
    private readonly IRiskModelPredictor _predictor;

    public AblationRiskExplainer(IRiskModelPredictor predictor)
        => _predictor = predictor ?? throw new ArgumentNullException(nameof(predictor));

    public bool IsAvailable => true;

    public RiskExplanation? Explain(RiskModelInput input, RiskExplanationBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(baseline);

        var descriptor = _predictor.Descriptor;
        if (!string.Equals(input.FeatureSchemaVersion, descriptor.FeatureSchemaVersion, StringComparison.Ordinal))
            return null;
        if (!string.Equals(baseline.FeatureSchemaVersion, descriptor.FeatureSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The explanation baseline was built for a different feature schema than the loaded model.");
        }

        var schema = RiskFeatureSchemas.GetRequired(input.FeatureSchemaVersion);
        var featureNames = schema.FeatureNames;
        var actual = _predictor.Predict(input).Probability;

        var factors = new List<RiskFactor>(featureNames.Count);
        for (var index = 0; index < featureNames.Count; index++)
        {
            var name = featureNames[index];
            if (!baseline.TryGetValue(name, out var baselineValue))
                continue;

            var studentValue = input.FeatureValues[index];

            // A feature already at the baseline cannot have moved the score away from it. Skipping
            // the model call is not just an optimisation: reporting a zero contribution for it
            // would pad the explanation with rows that say nothing.
            if (studentValue.Equals(baselineValue))
                continue;

            var swapped = input.FeatureValues.ToArray();
            swapped[index] = baselineValue;
            var withoutFeature = _predictor.Predict(new RiskModelInput(input.FeatureSchemaVersion, swapped)).Probability;

            factors.Add(new RiskFactor(
                name,
                RiskFeatureVocabulary.Describe(name),
                studentValue,
                baselineValue,
                actual - withoutFeature));
        }

        return new RiskExplanation(
            actual,
            descriptor.ModelVersion,
            descriptor.FeatureSchemaVersion,
            factors,
            baseline.Description,
            DescribeBoundary(actual, descriptor.QueueThreshold, factors));
    }

    /// <summary>
    /// States where the model's own decision boundary sits relative to this student.
    /// </summary>
    /// <remarks>
    /// Phrased strictly as a property of the model, never as advice. "This student would fall below
    /// the review threshold if X matched a typical student" is a true statement about a decision
    /// surface. "If X improves, their risk will fall" would be a causal promise the model cannot
    /// make: it learned association from observational data, and clicking more does not cause
    /// anyone to stay enrolled.
    /// </remarks>
    private static string? DescribeBoundary(
        double probability,
        double queueThreshold,
        IReadOnlyList<RiskFactor> factors)
    {
        if (probability < queueThreshold) return null;

        var largest = factors
            .Where(factor => factor.Direction == RiskFactorDirection.Raising)
            .OrderByDescending(factor => factor.Magnitude)
            .FirstOrDefault();
        if (largest is null) return null;

        return probability - largest.Contribution < queueThreshold
            ? $"On its own, {largest.DisplayName} accounts for this student being above the review " +
              "threshold: with that one value at the comparison level, the model would place them below it. " +
              "This describes the model's boundary, not a change that would alter the student's outcome."
            : $"The largest single factor is {largest.DisplayName}, but the model would still place this " +
              "student above the review threshold without it. No one feature explains the score on its own.";
    }
}
