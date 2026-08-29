using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

public interface IRiskSnapshotAdministrationService
{
    Task<RiskSnapshotStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<RiskSnapshotImportResultDto> ImportCsvAsync(
        Stream csv,
        string fileName,
        string actorUserId,
        CancellationToken cancellationToken = default);
    Task<RiskScoringRunDto> RunNowAsync(
        string actorUserId,
        CancellationToken cancellationToken = default);
}
