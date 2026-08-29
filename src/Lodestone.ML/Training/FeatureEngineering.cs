using Microsoft.ML;

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
        => _mlContext.Transforms.Concatenate(
                RawFeaturesColumnName,
                Models.StudentActivityFeatures.FeatureNames.ToArray())
            .Append(_mlContext.Transforms.NormalizeMeanVariance(
                FeaturesColumnName,
                RawFeaturesColumnName));
}
