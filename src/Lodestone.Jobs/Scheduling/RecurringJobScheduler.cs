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

        recurringJobs.AddOrUpdate<NudgeNotificationJob>(
            "nudge-dispatch", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily);

        recurringJobs.AddOrUpdate<BookingReminderJob>(
            "booking-reminders", job => job.ExecuteAsync(CancellationToken.None), Cron.Hourly);

        recurringJobs.AddOrUpdate<ForumModerationJob>(
            "forum-moderation", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily);

        recurringJobs.AddOrUpdate<CrisisResourceEscalationJob>(
            "crisis-escalation", job => job.ExecuteAsync(CancellationToken.None), "*/15 * * * *");
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
