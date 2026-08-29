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
}
