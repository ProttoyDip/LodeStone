using Lodestone.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lodestone.Web.Health;

/// <summary>
/// Reports whether risk scoring is intentionally disabled or has a validated model ready.
/// The check deliberately depends only on the Application-owned predictor boundary.
/// </summary>
public sealed class RiskModelHealthCheck(
    IConfiguration configuration,
    IRiskModelPredictor predictor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("MachineLearning:Enabled", false))
        {
            return Task.FromResult(HealthCheckResult.Healthy("Risk scoring is disabled."));
        }

        try
        {
            var descriptor = predictor.Descriptor;
            return Task.FromResult(HealthCheckResult.Healthy(
                "The validated risk model is available.",
                new Dictionary<string, object>
                {
                    ["modelVersion"] = descriptor.ModelVersion,
                    ["featureSchemaVersion"] = descriptor.FeatureSchemaVersion
                }));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Risk scoring is enabled, but no validated model is available.",
                exception));
        }
    }
}
