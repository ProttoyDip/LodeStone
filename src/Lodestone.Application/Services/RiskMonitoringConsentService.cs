using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public sealed class RiskMonitoringConsentService : IRiskMonitoringConsentService
{
    private readonly IRiskMonitoringConsentRepository _repository;

    public RiskMonitoringConsentService(IRiskMonitoringConsentRepository repository)
        => _repository = repository;

    public Task<RiskMonitoringConsentDto?> GetAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => _repository.GetByUserIdAsync(RequiredUserId(userId), cancellationToken);

    public Task<RiskMonitoringConsentDto> SetAsync(
        string userId,
        bool isConsented,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserId = RequiredUserId(userId);
        return _repository.SetByUserIdAsync(
            normalizedUserId,
            isConsented,
            normalizedUserId,
            cancellationToken);
    }

    private static string RequiredUserId(string userId)
        => string.IsNullOrWhiteSpace(userId)
            ? throw new ArgumentException("A user identifier is required.", nameof(userId))
            : userId.Trim();
}
