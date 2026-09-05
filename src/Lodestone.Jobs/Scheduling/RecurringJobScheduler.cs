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

        // Manual in-app nudges are visible immediately and deliberately have no background
        // delivery side effect in this release, so this schedule stays removed unconditionally.
        recurringJobs.RemoveIfExists("nudge-dispatch");

        // The supporting sweeps are implemented but stay off unless explicitly enabled: each one
        // either emails students or raises staff alerts. A deployment must opt in.
        var maintenance = configuration.GetSection(MaintenanceJobOptions.SectionName)
            .Get<MaintenanceJobOptions>() ?? new MaintenanceJobOptions();
        var maintenanceTimeZone = ResolveTimeZone(maintenance.TimeZoneId);

        Apply<BookingReminderJob>(recurringJobs, "booking-reminders", maintenance.BookingReminders, maintenanceTimeZone);
        Apply<ForumModerationJob>(recurringJobs, "forum-moderation", maintenance.ForumModeration, maintenanceTimeZone);
        Apply<CrisisResourceEscalationJob>(recurringJobs, "crisis-escalation", maintenance.CrisisEscalation, maintenanceTimeZone);
    }

    /// <summary>
    /// Registers a sweep when it is enabled with a usable cron, and otherwise removes any schedule
    /// an earlier deployment left behind — so disabling a job in configuration actually stops it.
    /// </summary>
    private static void Apply<TJob>(
        IRecurringJobManager recurringJobs,
        string jobId,
        MaintenanceJobOptions.JobSchedule schedule,
        TimeZoneInfo timeZone)
        where TJob : IMaintenanceJob
    {
        if (!schedule.Enabled || string.IsNullOrWhiteSpace(schedule.Cron))
        {
            recurringJobs.RemoveIfExists(jobId);
            return;
        }

        recurringJobs.AddOrUpdate<TJob>(
            jobId,
            job => job.ExecuteAsync(CancellationToken.None),
            schedule.Cron,
            new RecurringJobOptions { TimeZone = timeZone });
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
