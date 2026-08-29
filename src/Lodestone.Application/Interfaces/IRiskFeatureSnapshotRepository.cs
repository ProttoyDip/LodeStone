using Lodestone.Application.DTOs.Risk;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

public interface IRiskFeatureSnapshotRepository
{
    Task<RiskFeatureSnapshot?> GetByIdForScoringAsync(
        int snapshotId,
        DateTime asOfUtc,
        int maximumAgeDays,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetPendingIdsAsync(
        RiskModelDescriptor descriptor,
        DateTime asOfUtc,
        int maximumAgeDays,
        int? studentProfileId = null,
        CancellationToken cancellationToken = default);
    Task<RiskSnapshotImportResultDto> ImportAsync(
        string fileName,
        string fileSha256,
        IReadOnlyList<RiskFeatureSnapshotImportDto> rows,
        IReadOnlyList<RiskSnapshotImportErrorDto> parseErrors,
        string actorUserId,
        CancellationToken cancellationToken = default);
    Task<RiskSnapshotStatusDto> GetStatusAsync(
        RiskModelDescriptor? descriptor,
        string? modelUnavailableReason,
        DateTime asOfUtc,
        int maximumAgeDays,
        RiskScoringRunDto? latestRun,
        CancellationToken cancellationToken = default);
}
