using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lodestone.Web.Health;

/// <summary>Readiness probe for the application's primary EF Core database.</summary>
public sealed class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await dbContext.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Healthy("The primary database is reachable.");

            return HealthCheckResult.Unhealthy("The primary database is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Do not attach provider exceptions: connection strings and database host details
            // must never be emitted through a public readiness endpoint.
            return HealthCheckResult.Unhealthy(
                $"The primary database readiness check failed ({exception.GetType().Name}).");
        }
    }
}
