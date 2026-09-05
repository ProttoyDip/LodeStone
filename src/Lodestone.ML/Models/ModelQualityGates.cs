namespace Lodestone.ML.Models;

/// <summary>
/// Fixed publication gates. Command-line callers may repeat these values but cannot lower them.
/// </summary>
public static class ModelQualityGates
{
    public const double MinimumAreaUnderRocCurve = .70;
    public const double MinimumRecall = .70;
    public const double MinimumPrecision = .30;

    public static bool Passes(ModelMetrics? metrics)
        => metrics is not null
           && metrics.AreaUnderRocCurve + 1e-12 >= MinimumAreaUnderRocCurve
           && metrics.Recall + 1e-12 >= MinimumRecall
           && metrics.Precision + 1e-12 >= MinimumPrecision;
}
