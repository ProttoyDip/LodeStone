using Lodestone.Jobs.BackgroundJobs;
using Lodestone.Jobs.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.Jobs;

/// <summary>Registers Hangfire and all background job types.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHangfireJobs(configuration);
        services.Configure<RiskScoringJobOptions>(
            configuration.GetSection(RiskScoringJobOptions.SectionName));

        services.AddScoped<WeeklyRiskScoringJob>();
        services.AddScoped<NudgeNotificationJob>();
        services.AddScoped<BookingReminderJob>();
        services.AddScoped<ForumModerationJob>();
        services.AddScoped<CrisisResourceEscalationJob>();

        return services;
    }
}
