using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Risk;

public record RiskQueueItemDto(
    int QueueEntryId,
    int StudentProfileId,
    string StudentName,
    RiskLevel Level,
    bool IsResolved,
    DateTime CreatedAtUtc,
    string CourseKey = "",
    double Probability = 0,
    DateTime ScoredAtUtc = default,
    string RowVersionToken = "",
    string? StudentNumber = null,
    float ActiveDayRate = 0,
    float DaysSinceLastAccess = 0,
    float LateOrMissingAssignmentCount = 0);
