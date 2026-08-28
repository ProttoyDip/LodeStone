using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Email;
using Lodestone.Infrastructure.Identity;
using Lodestone.Infrastructure.Repositories;
using Lodestone.Infrastructure.Services;
using Lodestone.Infrastructure.Security;
using Lodestone.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.Infrastructure;

/// <summary>Registers EF Core, Identity, repositories, email and security services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString(AppConstants.DefaultConnectionStringName)));

        services.AddLodestoneIdentity();

        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<EncryptionSettings>(configuration.GetSection(EncryptionSettings.SectionName));

        // Application contracts implemented here.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IStudentDashboardService, StudentDashboardService>();
        services.AddScoped<ICounselorProvisioningService, CounselorProvisioningService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Repositories — interface-mapped so Application services can depend on abstractions.
        services.AddScoped(typeof(GenericRepository<>));
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<RiskScoreRepository>();
        services.AddScoped<CounselorQueueRepository>();
        services.AddScoped<ICrisisResourceRepository, CrisisResourceRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IStudentProfileRepository, StudentProfileRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IForumRepository, ForumRepository>();

        services.AddDataProtection();
        services.AddScoped<DataProtectionService>();

        return services;
    }
}
