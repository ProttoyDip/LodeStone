namespace Lodestone.Application.DTOs.Risk;

public sealed record RiskMonitoringConsentDto(
    int StudentProfileId,
    bool IsConsented,
    string PolicyVersion,
    DateTime? ConsentedAtUtc,
    DateTime? WithdrawnAtUtc);
