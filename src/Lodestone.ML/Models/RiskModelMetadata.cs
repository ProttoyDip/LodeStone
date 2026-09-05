namespace Lodestone.ML.Models;

/// <summary>Sidecar metadata required to load a saved model safely.</summary>
public sealed class RiskModelMetadata
{
    public const string CurrentMetadataSchemaVersion = "risk-model-metadata-v2";

    public string MetadataSchemaVersion { get; set; } = CurrentMetadataSchemaVersion;
    public string SchemaVersion { get; set; } = StudentActivityFeatures.SchemaVersion;
    public string ModelVersion { get; set; } = string.Empty;
    public List<string> FeatureNames { get; set; } = StudentActivityFeatures.FeatureNames.ToList();
    public float DecisionThreshold { get; set; }
    public DateTime TrainedAtUtc { get; set; }
    public int Seed { get; set; }
    public int ObservationWindowDays { get; set; }
    public int PredictionWindowDays { get; set; }
    public int ObservationStrideDays { get; set; }
    public int TrainingStudentCount { get; set; }
    public int ValidationStudentCount { get; set; }
    public int TestStudentCount { get; set; }
    public int TrainingRowCount { get; set; }
    public int ValidationRowCount { get; set; }
    public int TestRowCount { get; set; }
    public string ModelSha256 { get; set; } = string.Empty;
    public string PublicationId { get; set; } = string.Empty;
    public bool EligibleForRuntimeIntegration { get; set; }
    public string ModelAlgorithm { get; set; } = string.Empty;
    public Dictionary<string, string> Hyperparameters { get; set; } = new(StringComparer.Ordinal);
    public string TrainingProtocolVersion { get; set; } = "withdrawal-risk-training-v2";
    public string? SourceUrl { get; set; }
    public string? SourceSha256 { get; set; }
    public ModelMetrics ValidationMetrics { get; set; } = new();
    public ModelMetrics TestMetrics { get; set; } = new();
}

public sealed class TrainingReport
{
    public string ExperimentName { get; set; } = "train";
    public string TrainingProtocolVersion { get; set; } = "withdrawal-risk-training-v2";
    public string ModelVersion { get; set; } = string.Empty;
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public DateTime TrainedAtUtc { get; set; }
    public string ModelPath { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public string ModelSha256 { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? SourceSha256 { get; set; }
    public DatasetProvenanceSummary DatasetProvenance { get; set; } = new();
    public DatasetSplitSummary Split { get; set; } = new();
    public ModelMetrics ValidationMetrics { get; set; } = new();
    public ModelMetrics? TestMetrics { get; set; }
    public string TestEvaluationStatus { get; set; } = "NotEvaluated";
    public IReadOnlyList<ThresholdCurvePoint> ThresholdCurve { get; set; } = [];
    public IReadOnlyList<CrossValidationCandidateResult> CrossValidation { get; set; } = [];
    public IReadOnlyList<FeatureDriftSummary> FeatureDrift { get; set; } = [];
    public QualityGateResult QualityGate { get; set; } = new();
}

public sealed class DatasetSplitSummary
{
    public int TrainingStudents { get; set; }
    public int ValidationStudents { get; set; }
    public int TestStudents { get; set; }
    public int TrainingRows { get; set; }
    public int ValidationRows { get; set; }
    public int TestRows { get; set; }
    public string TrainingStudentHash { get; set; } = string.Empty;
    public string ValidationStudentHash { get; set; } = string.Empty;
    public string TestStudentHash { get; set; } = string.Empty;
    public int RandomSeed { get; set; }
}

public sealed class QualityGateResult
{
    public double MinimumAreaUnderRocCurve { get; set; }
    public double MinimumRecall { get; set; }
    public double MinimumPrecision { get; set; }
    public bool ValidationPassed { get; set; }
    public bool TestPassed { get; set; }
    public bool Passed { get; set; }
}

public sealed class DatasetProvenanceSummary
{
    public string? SourceUrl { get; set; }
    public string? SourceSha256 { get; set; }
    public string DatasetDirectoryHash { get; set; } = string.Empty;
}

public sealed class CrossValidationCandidateResult
{
    public string CandidateId { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public Dictionary<string, string> Hyperparameters { get; set; } = new(StringComparer.Ordinal);
    public double MeanAreaUnderRocCurve { get; set; }
    public double MeanAreaUnderPrecisionRecallCurve { get; set; }
    public double MeanRecall { get; set; }
    public double MeanPrecision { get; set; }
    public double MeanF1Score { get; set; }
    public int FoldCount { get; set; }
    public bool IsUsable { get; set; } = true;
    /// <summary>Sanitized trainer failure type when a bounded candidate cannot fit this dataset.</summary>
    public string? FailureReason { get; set; }
}

public sealed class FeatureDriftSummary
{
    public string FeatureName { get; set; } = string.Empty;
    public double ValidationPopulationStabilityIndex { get; set; }
    /// <summary>
    /// Unavailable when validation fails: the locked test partition is deliberately not examined
    /// until a validation-selected candidate has cleared every fixed gate.
    /// </summary>
    public double? TestPopulationStabilityIndex { get; set; }
}

public sealed class ThresholdCurvePoint
{
    public double Threshold { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public int TruePositive { get; set; }
    public int FalsePositive { get; set; }
    public int TrueNegative { get; set; }
    public int FalseNegative { get; set; }
}
