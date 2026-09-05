using Microsoft.ML.Data;

namespace Lodestone.ML.Models;

/// <summary>A labeled rolling observation used only by the offline trainer.</summary>
public sealed class StudentActivityObservation : StudentActivityFeatures
{
    [ColumnName("Label")]
    public bool IsAtRisk { get; set; }

    public float ExampleWeight { get; set; } = 1f;

    [NoColumn]
    public string StudentGroupKey { get; set; } = string.Empty;

    [NoColumn]
    public string EnrollmentKey { get; set; } = string.Empty;

    [NoColumn]
    public int ObservationDay { get; set; }

    /// <summary>Training-only course/presentation key used for leakage-safe cohort calibration.</summary>
    [NoColumn]
    public string CoursePresentationKey { get; set; } = string.Empty;

    /// <summary>
    /// Training-only day on which withdrawal occurred, if the label is positive. It is never used
    /// as a model feature and exists solely to report lead-time statistics after evaluation.
    /// </summary>
    [NoColumn]
    public int? WithdrawalDay { get; set; }
}
