namespace Lodestone.ML.Models;

/// <summary>
/// Stable, read-only runtime status for health checks and administrative diagnostics. A disabled
/// model is healthy because it is an intentional configuration state; an enabled model that
/// cannot be validated is unhealthy and always fails closed.
/// </summary>
public sealed record RiskModelStatus(
    bool IsEnabled,
    bool IsAvailable,
    string? ModelVersion,
    string? UnavailableReason)
{
    public bool IsHealthy => !IsEnabled || IsAvailable;

    public static RiskModelStatus Disabled()
        => new(false, false, null, "Machine learning is disabled by configuration.");

    public static RiskModelStatus Unavailable(string reason)
        => new(true, false, null, reason);

    public static RiskModelStatus Available(string version)
        => new(true, true, version, null);
}

public interface IRiskModelStatusProvider
{
    RiskModelStatus Status { get; }
}

internal sealed class RiskModelStatusProvider(RiskModelStatus status) : IRiskModelStatusProvider
{
    public RiskModelStatus Status { get; } = status;
}
