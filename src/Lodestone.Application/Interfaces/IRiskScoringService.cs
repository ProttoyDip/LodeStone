using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

/// <summary>Orchestrates risk scoring; delegates model inference to Lodestone.ML.</summary>
public interface IRiskScoringService
{
    Task<RiskScoreDto> ScoreStudentAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task ScoreAllStudentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RiskQueueItemDto>> GetOpenQueueAsync(CancellationToken cancellationToken = default);
    Task<RiskScoringResultDto> ScoreSnapshotAsync(
        int snapshotId,
        int? scoringRunId = null,
        CancellationToken cancellationToken = default);
    Task<RiskScoringRunDto> RunPendingSnapshotsAsync(
        string? actorUserId = null,
        CancellationToken cancellationToken = default);
}
