namespace Lodestone.Application.DTOs.Risk;

/// <summary>The canonical feature schema currently accepted by the withdrawal model.</summary>
public static class RiskFeatureSchema
{
    public const string Withdrawal28DayV1 = "withdrawal-28d-v1";
    public const int Withdrawal28DayObservedDays = 28;
}

public static class RiskMonitoringPolicy
{
    public const string CurrentVersion = "v1";
}

public static class RiskScoringPolicy
{
    public const int MaximumSnapshotAgeDays = 8;
}

/// <summary>Framework-neutral model input for one student/course/window snapshot.</summary>
public sealed record RiskModelInput(
    float ActiveDayRate,
    float ActivitySpanDays,
    float DaysSinceLastAccess,
    float ForumInteractionCount,
    float CourseInteractionCount,
    float LateOrMissingAssignmentCount);

/// <summary>Framework-neutral inference result. Probability is validated by Application.</summary>
public sealed record RiskModelPrediction(double Probability);

/// <summary>Metadata that binds a model artifact to its feature schema and queue threshold.</summary>
public sealed record RiskModelDescriptor(
    string ModelVersion,
    string FeatureSchemaVersion,
    int ObservedDays,
    double QueueThreshold);
