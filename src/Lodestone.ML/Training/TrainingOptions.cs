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
    public int Seed { get; init; } = 42;
    public double TrainingFraction { get; init; } = 0.70;
    public double ValidationFraction { get; init; } = 0.15;
    public double TestFraction { get; init; } = 0.15;
    public double MinimumTestAreaUnderRocCurve { get; init; } = 0.70;
    public double MinimumRecall { get; init; } = 0.70;
    public double MinimumPrecision { get; init; } = 0.30;

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
