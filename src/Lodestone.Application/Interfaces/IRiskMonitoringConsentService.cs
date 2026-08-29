using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

public interface IRiskMonitoringConsentService
{
    Task<RiskMonitoringConsentDto?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task<RiskMonitoringConsentDto> SetAsync(
        string userId,
        bool isConsented,
        CancellationToken cancellationToken = default);
}
