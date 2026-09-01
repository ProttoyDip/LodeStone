using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;

namespace Lodestone.Web.ViewModels.Risk;

/// <summary>
/// Web-facing operational state for the configured risk model. It deliberately exposes only
/// staff-safe model metadata and scoring-run totals; student views never receive this model.
/// </summary>
public sealed class RiskRuntimeStatusViewModel
{
    public required RiskModelStatus ModelStatus { get; init; }
    public RiskSnapshotStatusDto? SnapshotStatus { get; init; }
    public string? StatusError { get; init; }

    public RiskScoringRunDto? LatestRun => SnapshotStatus?.LatestRun;

    public string StateLabel => ModelStatus switch
    {
        { IsEnabled: false } => "Disabled",
        { IsAvailable: true } => "Available",
        _ => "Unavailable"
    };

    public string StateTone => ModelStatus switch
    {
        { IsEnabled: false } => "info",
        { IsAvailable: true } => "positive",
        _ => "critical"
    };

    public string StateDescription => ModelStatus switch
    {
        { IsEnabled: false } =>
            "Risk scoring is disabled by configuration. No model-based scores or new queue cases can be created.",
        { IsAvailable: true } =>
            "A validated model is loaded for this application process. Scoring remains consent-gated and requires a verified student number.",
        _ =>
            "Risk scoring is unavailable. No scores or new queue cases can be created until a validated model is loaded."
    };

    public string? UnavailableReason =>
        StatusError ?? SnapshotStatus?.ModelUnavailableReason ?? ModelStatus.UnavailableReason;

    public string ModelVersion => ModelStatus.ModelVersion ?? "Not loaded";

    public string FeatureSchemaVersion => SnapshotStatus?.Model?.FeatureSchemaVersion
        ?? ModelStatus.FeatureSchemaVersion
        ?? "Not loaded";
}
