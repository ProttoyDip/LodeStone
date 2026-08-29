namespace Lodestone.ML.Models;

/// <summary>Sidecar metadata required to load a saved model safely.</summary>
public sealed class RiskModelMetadata
{
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
    public string? SourceUrl { get; set; }
    public string? SourceSha256 { get; set; }
    public ModelMetrics ValidationMetrics { get; set; } = new();
    public ModelMetrics TestMetrics { get; set; } = new();
}

public sealed class TrainingReport
{
    public string ModelVersion { get; set; } = string.Empty;
    public DateTime TrainedAtUtc { get; set; }
    public string ModelPath { get; set; } = string.Empty;
    public string MetadataPath { get; set; } = string.Empty;
    public string ModelSha256 { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? SourceSha256 { get; set; }
    public DatasetSplitSummary Split { get; set; } = new();
    public ModelMetrics ValidationMetrics { get; set; } = new();
    public ModelMetrics TestMetrics { get; set; } = new();
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
}

public sealed class QualityGateResult
{
    public double MinimumAreaUnderRocCurve { get; set; }
    public double MinimumRecall { get; set; }
    public double MinimumPrecision { get; set; }
    public bool Passed { get; set; }
}
