namespace Lodestone.ML.Models;

/// <summary>
/// Runtime-side evidence that a particular model/metadata pair was published only after both
/// fixed quality gates passed. It is written atomically with the two application artifacts.
/// </summary>
public sealed class RiskModelPublicationManifest
{
    public const string CurrentManifestSchemaVersion = "risk-model-publication-v1";

    public string ManifestSchemaVersion { get; set; } = CurrentManifestSchemaVersion;
    public string PublicationId { get; set; } = string.Empty;
    public bool EligibleForRuntimeIntegration { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public int ObservationWindowDays { get; set; }
    public int PredictionWindowDays { get; set; }
    public int ObservationStrideDays { get; set; }
    public List<string> FeatureNames { get; set; } = [];
    public string ModelSha256 { get; set; } = string.Empty;
    public string MetadataSha256 { get; set; } = string.Empty;
    public string ModelAlgorithm { get; set; } = string.Empty;
    public QualityGateResult QualityGate { get; set; } = new();
}

public static class RiskModelPublicationPaths
{
    public static string GetManifestPath(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        return Path.ChangeExtension(Path.GetFullPath(modelPath), ".publication.json");
    }
}
