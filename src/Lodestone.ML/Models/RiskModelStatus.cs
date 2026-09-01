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
    /// <summary>Versioned input contract for the loaded, validated artifact.</summary>
    public string? FeatureSchemaVersion { get; init; }

    /// <summary>Immutable ID from the publication manifest; never a filesystem path.</summary>
    public string? PublicationId { get; init; }

    /// <summary>UTC timestamp from the accepted publication manifest.</summary>
    public DateTime? PublishedAtUtc { get; init; }

    public bool IsHealthy => !IsEnabled || IsAvailable;

    public static RiskModelStatus Disabled()
        => new(false, false, null, "Machine learning is disabled by configuration.");

    public static RiskModelStatus Unavailable(string reason)
        => new(true, false, null, reason);

    public static RiskModelStatus Available(
        string version,
        string? featureSchemaVersion = null,
        string? publicationId = null,
        DateTime? publishedAtUtc = null)
        => new(true, true, version, null)
        {
            FeatureSchemaVersion = featureSchemaVersion,
            PublicationId = publicationId,
            PublishedAtUtc = publishedAtUtc
        };
}

/// <summary>
/// Thrown by the fail-closed predictor when an enabled artifact is unavailable. The message is
/// deliberately sanitized so application logs and UI never disclose model filesystem details.
/// </summary>
public sealed class RiskModelUnavailableException : InvalidOperationException
{
    public RiskModelUnavailableException(string reason)
        : base($"Risk scoring is unavailable. {reason}")
    {
    }
}

public interface IRiskModelStatusProvider
{
    RiskModelStatus Status { get; }
}

internal sealed class RiskModelStatusProvider(RiskModelStatus status) : IRiskModelStatusProvider
{
    public RiskModelStatus Status { get; } = status;
}
