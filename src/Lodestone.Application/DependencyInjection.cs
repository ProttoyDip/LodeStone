using System.Reflection;
using FluentValidation;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lodestone.Application;

/// <summary>Registers Application-layer services and validators.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<IActivityLogService, ActivityLogService>();
        services.AddScoped<IRiskScoringService, RiskScoringService>();
        services.AddScoped<IRiskMonitoringConsentService, RiskMonitoringConsentService>();
        services.AddScoped<IRiskSnapshotAdministrationService, RiskSnapshotAdministrationService>();
        services.AddScoped<IStudentNumberVerificationService, StudentNumberVerificationService>();
        services.AddScoped<INudgeService, NudgeService>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ICounselorAvailabilityService, CounselorAvailabilityService>();
        services.AddScoped<ICounselorQueueService, CounselorQueueService>();
        services.AddScoped<ICrisisResourceService, CrisisResourceService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRiskQueueNotifier, NullRiskQueueNotifier>();
        return services;
    }
}
