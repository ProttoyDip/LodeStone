namespace Lodestone.Jobs.Scheduling;

/// <summary>
/// Scheduling for the supporting sweeps. Every job is disabled by default: each one either emails
/// students or raises staff alerts, so switching it on is an explicit operational decision rather
/// than something a deployment inherits silently.
/// </summary>
public sealed class MaintenanceJobOptions
{
    public const string SectionName = "MaintenanceJobs";

    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Emails students about their own confirmed sessions in the next 24 hours.</summary>
    public JobSchedule BookingReminders { get; set; } = new() { Cron = "0 7 * * *" };

    /// <summary>Alerts moderators that flagged discussions are waiting for review.</summary>
    public JobSchedule ForumModeration { get; set; } = new() { Cron = "0 8 * * *" };

    /// <summary>Alerts staff that high or critical cases have gone unreviewed for over a day.</summary>
    public JobSchedule CrisisEscalation { get; set; } = new() { Cron = "0 */6 * * *" };

    public sealed class JobSchedule
    {
        public bool Enabled { get; set; }
        public string Cron { get; set; } = string.Empty;
    }
}
