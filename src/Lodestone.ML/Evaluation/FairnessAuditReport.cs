namespace Lodestone.ML.Evaluation;

/// <summary>
/// Subgroup performance of a published risk model, measured on a held-out partition.
/// </summary>
/// <remarks>
/// <para>
/// This report measures <em>disparate impact</em>, not disparate treatment. The model is trained
/// on clickstream and assessment-timing features only; it never receives gender, age, deprivation
/// index, disability, prior education, or region. A gap between groups therefore does not mean the
/// model is reading a protected attribute -- it means the behavioural signal it does read carries
/// different predictive value for different groups, which is the harm that actually reaches a
/// student regardless of what the model was shown.
/// </para>
/// <para>
/// Rows are student-weeks, not students. One student contributes many correlated rows, so a group
/// of 10,000 rows is not 10,000 independent observations and the usual confidence intervals do not
/// apply. Student counts are reported alongside row counts for exactly this reason.
/// </para>
/// </remarks>
public sealed class FairnessAuditReport
{
    public string ModelVersion { get; set; } = string.Empty;
    public string FeatureSchemaVersion { get; set; } = string.Empty;
    public string ModelSha256 { get; set; } = string.Empty;
    public DateTime AuditedAtUtc { get; set; }

    /// <summary>Which held-out partition was scored: "test" or "validation".</summary>
    public string Partition { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the sorted student ids in the audited partition. Recomputed here from the same
    /// seed and fractions the model recorded, so it can be checked against the training report's
    /// hash to prove the audit scored the identical partition rather than a lookalike.
    /// </summary>
    public string PartitionStudentHash { get; set; } = string.Empty;
    public bool PartitionHashVerified { get; set; }

    public int Seed { get; set; }
    public int RowCount { get; set; }
    public int StudentCount { get; set; }
    public int PositiveCount { get; set; }
    public double BaseRate { get; set; }

    /// <summary>Rows whose enrollment could not be matched to a demographic record, and so were excluded.</summary>
    public int UnmatchedRowCount { get; set; }

    /// <summary>Smallest group, in rows, that is reported rather than suppressed.</summary>
    public int MinimumGroupRows { get; set; }

    public IReadOnlyList<FairnessOperatingPoint> OperatingPoints { get; set; } = [];
}

/// <summary>
/// Subgroup results at one decision threshold.
/// </summary>
/// <remarks>
/// A model has no single fairness profile: it has one per operating point. The threshold stamped
/// into the artifact is the one the quality gate was measured at, but the threshold configured for
/// the counselor queue is the one that decides whose name a human actually sees. Both are audited,
/// because a model can be even-handed at one and lopsided at the other.
/// </remarks>
public sealed class FairnessOperatingPoint
{
    public string Name { get; set; } = string.Empty;
    public double Threshold { get; set; }

    /// <summary>Overall metrics at this threshold, for comparison against each group.</summary>
    public FairnessGroupMetrics Overall { get; set; } = new();

    public IReadOnlyList<FairnessAttributeResult> Attributes { get; set; } = [];
}

/// <summary>Results for one protected attribute, such as gender or deprivation band.</summary>
public sealed class FairnessAttributeResult
{
    public string Attribute { get; set; } = string.Empty;
    public IReadOnlyList<FairnessGroupMetrics> Groups { get; set; } = [];
    public IReadOnlyList<SuppressedGroup> SuppressedGroups { get; set; } = [];

    /// <summary>
    /// Largest gap in recall between reported groups. Recall is the share of genuinely at-risk
    /// student-weeks the model catches, so this is the gap that decides who gets missed: the
    /// equal-opportunity difference.
    /// </summary>
    public double? RecallGap { get; set; }

    /// <summary>Largest gap in false-positive rate: who is disproportionately flagged when not at risk.</summary>
    public double? FalsePositiveRateGap { get; set; }

    /// <summary>Largest gap in precision: whether a flag means the same thing for every group.</summary>
    public double? PrecisionGap { get; set; }

    /// <summary>
    /// Lowest selection rate divided by the highest, across reported groups. This is the disparate
    /// impact ratio; the widely cited four-fifths rule treats values below 0.8 as worth explaining.
    /// It is a screening heuristic and not a legal threshold, and a low value here can be entirely
    /// legitimate when groups genuinely differ in underlying risk -- compare it against the base
    /// rates before drawing a conclusion.
    /// </summary>
    public double? SelectionRateRatio { get; set; }

    /// <summary>Largest gap in the underlying withdrawal rate, which bounds how equal any of the above could be.</summary>
    public double? BaseRateGap { get; set; }
}

/// <summary>Metrics for one group within an attribute.</summary>
public sealed class FairnessGroupMetrics
{
    public string Group { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int StudentCount { get; set; }
    public int PositiveCount { get; set; }

    /// <summary>Share of this group's student-weeks that genuinely precede a withdrawal.</summary>
    public double BaseRate { get; set; }

    /// <summary>Share of this group's student-weeks the model flags, whatever the truth.</summary>
    public double SelectionRate { get; set; }

    public double Recall { get; set; }
    public double Precision { get; set; }
    public double FalsePositiveRate { get; set; }
    public double FalseNegativeRate { get; set; }
    public double Accuracy { get; set; }

    /// <summary>Threshold-independent ranking quality within this group. Null when a group has one class only.</summary>
    public double? AreaUnderRocCurve { get; set; }

    public int TruePositive { get; set; }
    public int FalsePositive { get; set; }
    public int TrueNegative { get; set; }
    public int FalseNegative { get; set; }
}

/// <summary>
/// A group too small to report. Its size is disclosed but its metrics are not: on small groups the
/// numbers are unstable enough to mislead, and a per-group breakdown of a handful of people starts
/// to identify them.
/// </summary>
public sealed record SuppressedGroup(string Group, int RowCount, int StudentCount, string Reason);
