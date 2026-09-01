namespace Lodestone.Application.DTOs.Risk;

public sealed record RiskFeatureSnapshotImportDto(
    string StudentNumber,
    string CourseKey,
    DateTime WindowEndUtc,
    int ObservedDays,
    string FeatureSchemaVersion,
    float ActiveDayRate,
    float ActivitySpanDays,
    float DaysSinceLastAccess,
    float ForumInteractionCount,
    float CourseInteractionCount,
    float LateOrMissingAssignmentCount,
    int SourceRowNumber = 0,
    float? RecentActiveDayRate = null,
    float? PriorActiveDayRate = null,
    float? ActiveDayRateTrend = null,
    float? RecentCourseClickRate = null,
    float? PriorCourseClickRate = null,
    float? CourseClickRateTrend = null,
    float? InactivityStreakDays = null,
    float? AssessmentDueRate = null,
    float? AssessmentOnTimeRate = null,
    float? AssessmentLateOrMissingRate = null,
    float? CourseProgressRatio = null,
    float? CohortActivityPercentile = null)
{
    /// <summary>Returns values in the immutable order declared by FeatureSchemaVersion.</summary>
    public IReadOnlyList<float> GetFeatureValues()
    {
        var schema = RiskFeatureSchemas.GetRequired(FeatureSchemaVersion);
        return schema.Version switch
        {
            RiskFeatureSchema.Withdrawal28DayV1 =>
            [
                ActiveDayRate,
                ActivitySpanDays,
                DaysSinceLastAccess,
                ForumInteractionCount,
                CourseInteractionCount,
                LateOrMissingAssignmentCount
            ],
            RiskFeatureSchema.Withdrawal28DayV2 =>
            [
                Required(RecentActiveDayRate, nameof(RecentActiveDayRate)),
                Required(PriorActiveDayRate, nameof(PriorActiveDayRate)),
                Required(ActiveDayRateTrend, nameof(ActiveDayRateTrend)),
                Required(RecentCourseClickRate, nameof(RecentCourseClickRate)),
                Required(PriorCourseClickRate, nameof(PriorCourseClickRate)),
                Required(CourseClickRateTrend, nameof(CourseClickRateTrend)),
                Required(InactivityStreakDays, nameof(InactivityStreakDays)),
                Required(AssessmentDueRate, nameof(AssessmentDueRate)),
                Required(AssessmentOnTimeRate, nameof(AssessmentOnTimeRate)),
                Required(AssessmentLateOrMissingRate, nameof(AssessmentLateOrMissingRate)),
                Required(CourseProgressRatio, nameof(CourseProgressRatio)),
                Required(CohortActivityPercentile, nameof(CohortActivityPercentile))
            ],
            _ => throw new ArgumentException($"Unsupported risk feature schema '{FeatureSchemaVersion}'.")
        };
    }

    private static float Required(float? value, string name)
        => value ?? throw new InvalidOperationException($"{name} is required for withdrawal-28d-v2.");
}

public sealed record RiskFeatureSnapshotDto(
    int Id,
    int StudentProfileId,
    string CourseKey,
    DateTime WindowEndUtc,
    int ObservedDays,
    string FeatureSchemaVersion,
    string SourceFileName,
    string SourceFileSha256,
    float ActiveDayRate,
    float ActivitySpanDays,
    float DaysSinceLastAccess,
    float ForumInteractionCount,
    float CourseInteractionCount,
    float LateOrMissingAssignmentCount,
    IReadOnlyList<float>? VersionedFeatureValues = null);

public sealed record RiskSnapshotImportErrorDto(int RowNumber, string Message);

public sealed record RiskSnapshotImportResultDto(
    string FileName,
    int TotalRows,
    int ImportedRows,
    int DuplicateRows,
    int RejectedRows,
    IReadOnlyList<RiskSnapshotImportErrorDto> Errors);

public sealed record RiskSnapshotStatusDto(
    int SnapshotCount,
    int ConsentedStudentCount,
    int PendingSnapshotCount,
    DateTime? LatestWindowEndUtc,
    RiskModelDescriptor? Model,
    string? ModelUnavailableReason,
    RiskScoringRunDto? LatestRun);
