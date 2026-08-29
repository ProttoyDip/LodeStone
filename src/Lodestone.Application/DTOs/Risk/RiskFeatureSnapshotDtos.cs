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
    int SourceRowNumber = 0);

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
    float LateOrMissingAssignmentCount);

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
