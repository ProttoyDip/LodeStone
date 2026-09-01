using Hangfire;
using Lodestone.Jobs.BackgroundJobs;
using Microsoft.Extensions.Configuration;

namespace Lodestone.Jobs.Scheduling;

/// <summary>Registers the recurring Hangfire schedules. Called once at startup.</summary>
public static class RecurringJobScheduler
{
    public static void RegisterRecurringJobs(
        IRecurringJobManager recurringJobs,
        IConfiguration configuration,
        bool riskScoringEnabled)
    {
        if (riskScoringEnabled)
        {
            var options = configuration.GetSection(RiskScoringJobOptions.SectionName)
                .Get<RiskScoringJobOptions>() ?? new RiskScoringJobOptions();
            var timeZone = ResolveTimeZone(options.TimeZoneId);

            recurringJobs.AddOrUpdate<WeeklyRiskScoringJob>(
                "weekly-risk-scoring",
                job => job.ExecuteAsync(CancellationToken.None),
                options.Cron,
                new RecurringJobOptions { TimeZone = timeZone });
        }
        else
        {
            recurringJobs.RemoveIfExists("weekly-risk-scoring");
        }

        // These workflows have no complete, safety-reviewed implementation.  Remove
        // any jobs left by an older deployment regardless of configuration so a
        // production setting cannot turn a placeholder into a permanent failure.
        // Manual in-app nudges are visible immediately and deliberately have no
        // background delivery side effect in this release.
        recurringJobs.RemoveIfExists("nudge-dispatch");
        recurringJobs.RemoveIfExists("booking-reminders");
        recurringJobs.RemoveIfExists("forum-moderation");
        recurringJobs.RemoveIfExists("crisis-escalation");
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
