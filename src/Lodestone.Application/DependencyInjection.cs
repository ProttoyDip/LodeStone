using System.Reflection;
using FluentValidation;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.Application;

/// <summary>Registers Application-layer services, validators and AutoMapper profiles.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(Assembly.GetExecutingAssembly());
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services.AddScoped<IActivityLogService, ActivityLogService>();
        services.AddScoped<IRiskScoringService, RiskScoringService>();
        services.AddScoped<INudgeService, NudgeService>();
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ICounselorAvailabilityService, CounselorAvailabilityService>();
        services.AddScoped<ICounselorQueueService, CounselorQueueService>();
        services.AddScoped<ICrisisResourceService, CrisisResourceService>();
        return services;
    }
}
