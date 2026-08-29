namespace Lodestone.Jobs.Scheduling;

public sealed class RiskScoringJobOptions
{
    public const string SectionName = "RiskScoring";

    public string Cron { get; set; } = "0 2 * * 1";
    public string TimeZoneId { get; set; } = "UTC";
}
