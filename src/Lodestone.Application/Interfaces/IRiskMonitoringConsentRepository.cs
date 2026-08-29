using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

public interface IRiskMonitoringConsentRepository
{
    Task<RiskMonitoringConsentDto?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<RiskMonitoringConsentDto> SetByUserIdAsync(
        string userId,
        bool isConsented,
        string? actorUserId,
        CancellationToken cancellationToken = default);
}
