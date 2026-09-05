using Lodestone.ML.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lodestone.Web.Health;

/// <summary>
/// Reports whether risk scoring is intentionally disabled or has a validated model ready.
/// The check deliberately depends only on the Application-owned predictor boundary.
/// </summary>
public sealed class RiskModelHealthCheck(
    IRiskModelStatusProvider statusProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = statusProvider.Status;
        if (!status.IsEnabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Risk scoring is disabled."));
        }

        if (status.IsAvailable)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "The validated risk model is available.",
                new Dictionary<string, object>
                {
                    ["modelVersion"] = status.ModelVersion ?? string.Empty,
                    ["featureSchemaVersion"] = status.FeatureSchemaVersion ?? string.Empty,
                    ["publicationId"] = status.PublicationId ?? string.Empty
                }));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "Risk scoring is enabled, but no validated model is available."));
    }
}
