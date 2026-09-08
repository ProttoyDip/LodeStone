using System.Security.Cryptography;
using System.Text;
using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.ML;

namespace Lodestone.ML.Evaluation;

/// <summary>Options for one fairness audit run.</summary>
public sealed class FairnessAuditOptions
{
    public required string DataDirectory { get; init; }
    public required string ModelPath { get; init; }
    public required string MetadataPath { get; init; }

    /// <summary>"test" (default) or "validation". See <see cref="FairnessAuditor"/> on which to use.</summary>
    public string Partition { get; init; } = "test";

    /// <summary>
    /// Additional operating points to audit beyond the artifact's own threshold, as name/threshold
    /// pairs. The deployed queue threshold belongs here: it, not the artifact threshold, decides
    /// whose name reaches a counselor.
    /// </summary>
    public IReadOnlyList<(string Name, double Threshold)> AdditionalOperatingPoints { get; init; } = [];

    /// <summary>Groups smaller than this many rows are suppressed rather than reported.</summary>
    public int MinimumGroupRows { get; init; } = 500;

    /// <summary>Groups with fewer than this many positive rows are suppressed: recall would be noise.</summary>
    public int MinimumGroupPositives { get; init; } = 20;

    /// <summary>Optional expected partition student hash, from the training report, to prove identity.</summary>
    public string? ExpectedPartitionStudentHash { get; init; }

    public double TrainingFraction { get; init; } = 0.70;
    public double ValidationFraction { get; init; } = 0.15;
}

/// <summary>
/// Measures how a published risk model performs across student subgroups it was never shown.
/// </summary>
/// <remarks>
/// <para>
/// The audit reconstructs the exact partition the model was evaluated on -- same seed, same
/// fractions, same cohort calibration fit on training only -- loads the published artifact, scores
/// the held-out rows, and joins each row back to the demographics in studentInfo.csv. It trains
/// nothing and publishes nothing.
/// </para>
/// <para>
/// <b>On which partition to use.</b> Test gives figures comparable to the model's headline metrics,
/// and auditing a frozen artifact does not "spend" the partition the way model selection would.
/// But that holds only while the audit stays a report. The moment a fairness result changes the
/// model -- reweighting, refitting, a different candidate -- the test partition has informed
/// selection and is no longer an honest estimate for the model that results. Explore on
/// validation; report on test once.
/// </para>
/// </remarks>
public sealed class FairnessAuditor
{
    private readonly MLContext _mlContext;
    private readonly OuladDataLoader _loader;

    public FairnessAuditor(MLContext mlContext, OuladDataLoader loader)
    {
        _mlContext = mlContext ?? throw new ArgumentNullException(nameof(mlContext));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public FairnessAuditReport Run(FairnessAuditOptions options, RiskModelMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        var partitionName = options.Partition?.Trim().ToLowerInvariant() ?? "test";
        if (partitionName is not ("test" or "validation"))
            throw new ArgumentOutOfRangeException(nameof(options), "Partition must be 'test' or 'validation'.");

        var schema = RiskFeatureSchemas.GetRequired(metadata.SchemaVersion);
        var observations = _loader.LoadObservations(options.DataDirectory, schema.Version);
        var split = GroupDataSplitter.Split(
            observations,
            metadata.Seed,
            options.TrainingFraction,
            options.ValidationFraction);

        // Fit the cohort percentile on training alone, exactly as the pipeline does. Fitting it on
        // the audited partition would leak that partition into its own features and quietly flatter
        // every number below.
        var rows = partitionName == "test" ? split.Test : split.Validation;
        if (GroupedCrossValidator.UsesCohortCalibration(schema))
        {
            CohortFeatureCalibrator.Fit(split.Training).Apply(rows);
        }

        var studentHash = ComputeStudentHash(rows.Select(row => row.StudentGroupKey).Distinct());
        if (!string.IsNullOrWhiteSpace(options.ExpectedPartitionStudentHash)
            && !string.Equals(studentHash, options.ExpectedPartitionStudentHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The reconstructed {partitionName} partition does not match the expected student hash. " +
                "The audit would be reporting on a different set of students than the model was evaluated on.");
        }

        var model = _mlContext.Model.Load(options.ModelPath, out _);

        // CreateEnumerable preserves row order, so scored[i] belongs to rows[i]. The demographics
        // are joined here rather than carried through the IDataView precisely so that no protected
        // attribute can ever reach the feature pipeline.
        var scored = _mlContext.Data
            .CreateEnumerable<ScoredObservation>(
                model.Transform(_mlContext.Data.LoadFromEnumerable(rows)),
                reuseRowObject: false)
            .ToArray();
        if (scored.Length != rows.Count)
            throw new InvalidDataException("The scored row count does not match the audited partition.");

        var demographics = ProtectedAttributeLoader.Load(options.DataDirectory);
        var audited = new List<AuditRow>(rows.Count);
        var unmatched = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            if (!demographics.TryGetValue(rows[index].EnrollmentKey, out var attributes))
            {
                unmatched++;
                continue;
            }

            audited.Add(new AuditRow(
                rows[index].StudentGroupKey,
                scored[index].Label,
                scored[index].Probability,
                attributes));
        }

        if (audited.Count == 0)
            throw new InvalidDataException("No audited row could be matched to a demographic record.");

        var operatingPoints = new List<(string Name, double Threshold)>
        {
            ("artifact-threshold", metadata.DecisionThreshold)
        };
        foreach (var point in options.AdditionalOperatingPoints)
        {
            if (!double.IsFinite(point.Threshold) || point.Threshold is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(options), $"Operating point '{point.Name}' is not a probability.");
            operatingPoints.Add(point);
        }

        var positiveCount = audited.Count(row => row.Label);
        return new FairnessAuditReport
        {
            ModelVersion = metadata.ModelVersion,
            FeatureSchemaVersion = metadata.SchemaVersion,
            ModelSha256 = metadata.ModelSha256,
            AuditedAtUtc = DateTime.UtcNow,
            Partition = partitionName,
            PartitionStudentHash = studentHash,
            PartitionHashVerified = !string.IsNullOrWhiteSpace(options.ExpectedPartitionStudentHash),
            Seed = metadata.Seed,
            RowCount = audited.Count,
            StudentCount = audited.Select(row => row.StudentKey).Distinct(StringComparer.Ordinal).Count(),
            PositiveCount = positiveCount,
            BaseRate = positiveCount / (double)audited.Count,
            UnmatchedRowCount = unmatched,
            MinimumGroupRows = options.MinimumGroupRows,
            OperatingPoints = operatingPoints
                .Select(point => BuildOperatingPoint(point.Name, point.Threshold, audited, options))
                .ToArray()
        };
    }

    private static FairnessOperatingPoint BuildOperatingPoint(
        string name,
        double threshold,
        IReadOnlyList<AuditRow> rows,
        FairnessAuditOptions options)
        => new()
        {
            Name = name,
            Threshold = threshold,
            Overall = Measure("(all students)", rows, threshold),
            Attributes = ProtectedAttributes.Names
                .Select(attribute => BuildAttribute(attribute, rows, threshold, options))
                .ToArray()
        };

    private static FairnessAttributeResult BuildAttribute(
        string attribute,
        IReadOnlyList<AuditRow> rows,
        double threshold,
        FairnessAuditOptions options)
    {
        var reported = new List<FairnessGroupMetrics>();
        var suppressed = new List<SuppressedGroup>();

        foreach (var group in rows.GroupBy(row => row.Attributes[attribute], StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var positives = members.Count(row => row.Label);
            var students = members.Select(row => row.StudentKey).Distinct(StringComparer.Ordinal).Count();

            if (members.Length < options.MinimumGroupRows)
            {
                suppressed.Add(new SuppressedGroup(
                    group.Key, members.Length, students,
                    $"fewer than {options.MinimumGroupRows} student-weeks"));
                continue;
            }

            if (positives < options.MinimumGroupPositives)
            {
                suppressed.Add(new SuppressedGroup(
                    group.Key, members.Length, students,
                    $"fewer than {options.MinimumGroupPositives} withdrawal student-weeks"));
                continue;
            }

            reported.Add(Measure(group.Key, members, threshold));
        }

        return new FairnessAttributeResult
        {
            Attribute = attribute,
            Groups = reported,
            SuppressedGroups = suppressed,
            RecallGap = FairnessMetrics.Gap(reported, metrics => metrics.Recall),
            FalsePositiveRateGap = FairnessMetrics.Gap(reported, metrics => metrics.FalsePositiveRate),
            PrecisionGap = FairnessMetrics.Gap(reported, metrics => metrics.Precision),
            BaseRateGap = FairnessMetrics.Gap(reported, metrics => metrics.BaseRate),
            SelectionRateRatio = FairnessMetrics.Ratio(reported, metrics => metrics.SelectionRate)
        };
    }

    /// <summary>
    /// Adds the one thing <see cref="FairnessMetrics"/> cannot know: how many distinct students
    /// stand behind these rows. Rows are student-weeks, so a group of 10,000 rows may be only a few
    /// hundred people, and reporting only the row count would overstate how solid the numbers are.
    /// </summary>
    private static FairnessGroupMetrics Measure(string group, IReadOnlyList<AuditRow> rows, double threshold)
    {
        var metrics = FairnessMetrics.Compute(
            group,
            rows.Select(row => new FairnessMetrics.Outcome(row.Label, row.Probability)).ToArray(),
            threshold);
        metrics.StudentCount = rows.Select(row => row.StudentKey).Distinct(StringComparer.Ordinal).Count();
        return metrics;
    }

    /// <summary>Matches TrainingPipeline's student hash exactly, so the two can be compared.</summary>
    private static string ComputeStudentHash(IEnumerable<string> students)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", students.OrderBy(value => value, StringComparer.Ordinal))))).ToLowerInvariant();

    private readonly record struct AuditRow(
        string StudentKey,
        bool Label,
        float Probability,
        ProtectedAttributes Attributes);
}
