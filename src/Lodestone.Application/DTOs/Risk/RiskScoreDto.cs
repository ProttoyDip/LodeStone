using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Risk;

public record RiskScoreDto(
    int StudentProfileId,
    string StudentName,
    double Probability,
    RiskLevel Level,
    DateTime ScoredAtUtc,
    int RiskScoreId = 0,
    int RiskFeatureSnapshotId = 0,
    string CourseKey = "",
    DateTime WindowEndUtc = default,
    string FeatureSchemaVersion = "",
    string ModelVersion = "");
