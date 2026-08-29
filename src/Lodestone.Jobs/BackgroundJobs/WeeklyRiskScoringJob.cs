using Hangfire;
using Lodestone.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>Recurring Hangfire job: recompute risk scores for all students weekly.</summary>
public class WeeklyRiskScoringJob
{
    private readonly IRiskScoringService _riskScoringService;
    private readonly ILogger<WeeklyRiskScoringJob> _logger;

    public WeeklyRiskScoringJob(
        IRiskScoringService riskScoringService,
        ILogger<WeeklyRiskScoringJob> logger)
        => (_riskScoringService, _logger) = (riskScoringService, logger);

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var run = await _riskScoringService.RunPendingSnapshotsAsync(cancellationToken: cancellationToken);
        _logger.LogInformation(
            "Risk scoring run {RunKey} completed with {ScoredCount} scored, {SkippedCount} skipped, and {FailedCount} failed snapshots.",
            run.RunKey,
            run.ScoredCount,
            run.SkippedCount,
            run.FailedCount);
    }
}
