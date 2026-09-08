namespace Lodestone.Application.DTOs.Risk;

/// <summary>
/// What monitoring currently holds about one student, phrased for the student. Counts and dates
/// only: no probabilities, no risk bands, no feature values. The single outcome exposed is whether
/// a counselor was asked to check in, because that is the only effect the student can experience.
/// </summary>
public sealed record StudentMonitoringSummaryDto(
    int SnapshotCount,
    int DistinctCourseCount,
    DateTime? EarliestWindowEndUtc,
    DateTime? LatestWindowEndUtc,
    int ImportCount,
    DateTime? LastImportedAtUtc,
    int ScoredSnapshotCount,
    DateTime? LastScoredAtUtc,
    bool CounselorCheckInSuggested,
    DateTime? CounselorCheckInLastSignaledAtUtc,
    bool CounselorCheckInResolved);

public sealed record RiskMonitoringConsentDto(
    int StudentProfileId,
    bool IsConsented,
    string PolicyVersion,
    DateTime? ConsentedAtUtc,
    DateTime? WithdrawnAtUtc);
