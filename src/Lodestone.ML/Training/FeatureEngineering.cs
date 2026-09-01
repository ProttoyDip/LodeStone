using Microsoft.ML;
using Lodestone.Application.DTOs.Risk;

namespace Lodestone.ML.Training;

/// <summary>Builds the transformation pipeline (normalization, feature concat) for training/scoring.</summary>
public class FeatureEngineering
{
    private readonly MLContext _mlContext;

    public FeatureEngineering(MLContext mlContext) => _mlContext = mlContext;

    public const string RawFeaturesColumnName = "RawFeatures";
    public const string FeaturesColumnName = "Features";

    /// <summary>
    /// Concatenates the versioned feature contract and learns normalization parameters when the
    /// returned estimator is fitted to training data. Callers must never fit it to validation or
    /// test data.
    /// </summary>
    public IEstimator<ITransformer> BuildPipeline()
        => BuildPipeline(RiskFeatureSchemas.Withdrawal28DayV1.FeatureNames);

    public IEstimator<ITransformer> BuildPipeline(IReadOnlyList<string> featureNames)
    {
        ArgumentNullException.ThrowIfNull(featureNames);
        if (featureNames.Count == 0 || featureNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A non-empty ordered feature list is required.", nameof(featureNames));

        return BuildPipelineCore(featureNames);
    }

    private IEstimator<ITransformer> BuildPipelineCore(IReadOnlyList<string> featureNames)
        => _mlContext.Transforms.Concatenate(
                RawFeaturesColumnName,
                featureNames.ToArray())
            .Append(_mlContext.Transforms.NormalizeMeanVariance(
                FeaturesColumnName,
                RawFeaturesColumnName));
}
