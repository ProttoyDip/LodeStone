using Lodestone.ML.Models;

namespace Lodestone.ML.Training;

public sealed class TrainingOptions
{
    public required string DataDirectory { get; init; }
    public required string ModelOutputPath { get; init; }
    public string? MetadataOutputPath { get; init; }
    public string? ReportOutputPath { get; init; }
    public string? ModelVersion { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceSha256 { get; init; }
    /// <summary>Registered feature schema trained and published by this run.</summary>
    public string FeatureSchemaVersion { get; init; } = Lodestone.Application.DTOs.Risk.RiskFeatureSchema.Withdrawal28DayV1;
    /// <summary>V2 enables grouped cross-validation and the bounded FastTree/LightGBM candidate grid.</summary>
    public bool UseV2Experiment { get; init; }
    public string ExperimentName { get; init; } = "train";
    public int Seed { get; init; } = 42;
    public double TrainingFraction { get; init; } = 0.70;
    public double ValidationFraction { get; init; } = 0.15;
    public double TestFraction { get; init; } = 0.15;
    // Kept for source compatibility. Values may be made stricter, never lowered below the
    // fixed publication gates in ModelQualityGates.
    public double MinimumTestAreaUnderRocCurve { get; init; } = ModelQualityGates.MinimumAreaUnderRocCurve;
    public double MinimumRecall { get; init; } = ModelQualityGates.MinimumRecall;
    public double MinimumPrecision { get; init; } = ModelQualityGates.MinimumPrecision;

    /// <summary>
    /// Recall headroom demanded of the validation-selected threshold, above <see cref="MinimumRecall"/>.
    /// Threshold selection maximises F1 subject to the recall floor, and because precision rises as
    /// recall falls, the optimum otherwise lands exactly on that floor with no margin -- so ordinary
    /// validation-to-test sampling variation decides whether the locked-test gate passes. Selecting
    /// against a raised floor keeps the published operating point clear of the gate it must satisfy.
    /// </summary>
    public double RecallSelectionMargin { get; init; } = .03;

    public string ResolveMetadataPath()
        => MetadataOutputPath ?? Path.ChangeExtension(ModelOutputPath, ".metadata.json");

    public string ResolveReportPath()
        => ReportOutputPath ?? Path.ChangeExtension(ModelOutputPath, ".report.json");

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelOutputPath);
        if (ModelVersion is not null && string.IsNullOrWhiteSpace(ModelVersion))
            throw new ArgumentException("ModelVersion cannot be empty when specified.", nameof(ModelVersion));
        if (ModelVersion?.Trim().Length > 128)
            throw new ArgumentException("ModelVersion cannot exceed 128 characters.", nameof(ModelVersion));
        if (Seed < 0)
            throw new ArgumentOutOfRangeException(nameof(Seed), "Seed must be non-negative.");
        if (Math.Abs(TrainingFraction + ValidationFraction + TestFraction - 1d) > 1e-9)
            throw new ArgumentException("Training, validation and test fractions must add up to 1.0.");
        if (TrainingFraction <= 0 || ValidationFraction <= 0 || TestFraction <= 0)
            throw new ArgumentException("Training, validation and test fractions must all be positive.");
        ValidateRate(MinimumTestAreaUnderRocCurve, nameof(MinimumTestAreaUnderRocCurve));
        ValidateRate(MinimumRecall, nameof(MinimumRecall));
        ValidateRate(MinimumPrecision, nameof(MinimumPrecision));
        ValidateRate(RecallSelectionMargin, nameof(RecallSelectionMargin));
        if (MinimumRecall + RecallSelectionMargin > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RecallSelectionMargin),
                "The recall gate plus its selection margin cannot exceed 1.0.");
        }
        if (MinimumTestAreaUnderRocCurve < ModelQualityGates.MinimumAreaUnderRocCurve ||
            MinimumRecall < ModelQualityGates.MinimumRecall ||
            MinimumPrecision < ModelQualityGates.MinimumPrecision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumTestAreaUnderRocCurve),
                $"Publication gates cannot be lowered below AUC {ModelQualityGates.MinimumAreaUnderRocCurve:F2}, " +
                $"recall {ModelQualityGates.MinimumRecall:F2}, and precision {ModelQualityGates.MinimumPrecision:F2}.");
        }
        _ = Lodestone.Application.DTOs.Risk.RiskFeatureSchemas.GetRequired(FeatureSchemaVersion);
        if (UseV2Experiment
            && !string.Equals(FeatureSchemaVersion, Lodestone.Application.DTOs.Risk.RiskFeatureSchema.Withdrawal28DayV2, StringComparison.Ordinal)
            && !string.Equals(FeatureSchemaVersion, Lodestone.Application.DTOs.Risk.RiskFeatureSchema.Withdrawal28DayV3, StringComparison.Ordinal))
        {
            throw new ArgumentException("UseV2Experiment requires withdrawal-28d-v2 or withdrawal-28d-v3.", nameof(FeatureSchemaVersion));
        }
        if (string.IsNullOrWhiteSpace(ExperimentName) || ExperimentName.Trim().Length > 80)
            throw new ArgumentException("ExperimentName must contain 1-80 characters.", nameof(ExperimentName));
        if (!string.IsNullOrWhiteSpace(SourceSha256)
            && (SourceSha256.Trim().Length != 64 || !SourceSha256.Trim().All(Uri.IsHexDigit)))
        {
            throw new ArgumentException("SourceSha256 must be a 64-character hexadecimal SHA-256 value.", nameof(SourceSha256));
        }
    }

    private static void ValidateRate(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "Metric gates must be between zero and one.");
    }
}
