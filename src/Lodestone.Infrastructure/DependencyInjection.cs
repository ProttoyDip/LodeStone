using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Email;
using Lodestone.Infrastructure.Identity;
using Lodestone.Infrastructure.Repositories;
using Lodestone.Infrastructure.Services;
using Lodestone.Infrastructure.Security;
using Lodestone.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.Infrastructure;

/// <summary>Registers EF Core, Identity, repositories, email and security services.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Service key for the real SMTP sender, so a decorator can depend on it without the default
    /// <see cref="IEmailService"/> registration resolving back into itself.
    /// </summary>
    public const string SmtpEmailServiceKey = "smtp";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString(AppConstants.DefaultConnectionStringName)));

        services.AddLodestoneIdentity();

        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<EncryptionSettings>(configuration.GetSection(EncryptionSettings.SectionName));

        // Application contracts implemented here.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        // Keyed as well as default so a host can wrap the real sender without a resolution cycle.
        services.AddScoped<IEmailService, EmailService>();
        services.AddKeyedScoped<IEmailService, EmailService>(SmtpEmailServiceKey);
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IStudentDashboardService, StudentDashboardService>();
        services.AddScoped<ICounselorProvisioningService, CounselorProvisioningService>();
        services.AddScoped<IVolunteerProvisioningService, VolunteerProvisioningService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportDataProvider, ReportDataProvider>();

        // Repositories — interface-mapped so Application services can depend on abstractions.
        services.AddScoped(typeof(GenericRepository<>));
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IRiskScoringRepository, RiskScoreRepository>();
        services.AddScoped<ICounselorQueueRepository, CounselorQueueRepository>();
        services.AddScoped<IRiskMonitoringConsentRepository, RiskMonitoringConsentRepository>();
        services.AddScoped<IRiskFeatureSnapshotRepository, RiskFeatureSnapshotRepository>();
        services.AddScoped<IStudentNumberVerificationRepository, StudentNumberVerificationRepository>();
        services.AddScoped<ICrisisResourceRepository, CrisisResourceRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<INudgeRepository, NudgeRepository>();
        services.AddScoped<IStudentProfileRepository, StudentProfileRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IForumRepository, ForumRepository>();
        services.AddScoped<IVolunteerSupportRepository, VolunteerSupportRepository>();

        var encryptionSettings = configuration.GetSection(EncryptionSettings.SectionName)
            .Get<EncryptionSettings>() ?? new EncryptionSettings();
        var configuredKeyRingPath = string.IsNullOrWhiteSpace(encryptionSettings.KeyRingPath)
            ? Path.Combine("App_Data", "keys")
            : encryptionSettings.KeyRingPath;
        var keyRingPath = Path.GetFullPath(
            Path.IsPathRooted(configuredKeyRingPath)
                ? configuredKeyRingPath
                : Path.Combine(contentRootPath ?? AppContext.BaseDirectory, configuredKeyRingPath));
        Directory.CreateDirectory(keyRingPath);

        services.AddDataProtection()
            .SetApplicationName(string.IsNullOrWhiteSpace(encryptionSettings.ApplicationName)
                ? "Lodestone"
                : encryptionSettings.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddScoped<ISensitiveDataProtector, DataProtectionService>();
        services.AddScoped<JournalNoteProtectionMigrator>();

        return services;
    }
}
