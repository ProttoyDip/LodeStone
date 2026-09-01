using Hangfire;
using Lodestone.Application;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Infrastructure;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Identity;
using Lodestone.Infrastructure.Security;
using Lodestone.Jobs;
using Lodestone.Jobs.Scheduling;
using Lodestone.ML;
using Lodestone.Reporting;
using Lodestone.Web;
using Lodestone.Web.Configuration;
using Lodestone.Web.Health;
using Lodestone.Web.Hubs;
using Lodestone.Web.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var useHangfire = builder.Configuration.GetValue("Startup:UseHangfire", true);
var initializeDatabase = builder.Configuration.GetValue("Startup:InitializeDatabase", true);
var protectLegacyJournalNotes = builder.Configuration.GetValue("Startup:ProtectLegacyJournalNotes", true);
var requireProtectedJournalNotes = builder.Configuration.GetValue("Encryption:RequireProtectedJournalNotes", true);

// QuestPDF community licence (report generation lives in Lodestone.Reporting).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// ---- MVC + Web-only services ----
builder.Services.AddHttpsRedirection(options => options.HttpsPort = 5001);
var mvcBuilder = builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // Identity UI area
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}
builder.Services.AddSignalR();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services
    .AddOptions<PublicUrlSettings>()
    .Bind(builder.Configuration.GetSection(PublicUrlSettings.SectionName))
    .Validate(PublicUrlSettings.IsValid,
        "PublicUrl:BaseUrl must be an absolute HTTPS origin or path base without credentials, query, or fragment.")
    .ValidateOnStart();
builder.Services.AddSingleton<IPublicAccountLinkBuilder, PublicAccountLinkBuilder>();
builder.Services.AddHealthChecks()
    .AddCheck<RiskModelHealthCheck>("risk-model", tags: ["ready", "ml"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready", "database"]);

// ---- Authorization policies (defined in Infrastructure/Identity) ----
builder.Services.AddAuthorization(IdentityPolicySeeder.AddPolicies);

// ---- Clean Architecture layer registrations ----
builder.Services.AddApplication();
// Override Application's transport-neutral no-op with Web's authorized SignalR refresh signal.
builder.Services.AddScoped<IRiskQueueNotifier, SignalRRiskQueueNotifier>();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddMachineLearning(builder.Configuration, builder.Environment.ContentRootPath);
if (useHangfire)
{
    builder.Services.AddJobs(builder.Configuration);
}
builder.Services.AddReporting();

// ---- Auth cookie routing (points Identity at the MVC AccountController) ----
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

var app = builder.Build();

// ---- Middleware pipeline ----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
        headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
        headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
        if (!app.Environment.IsDevelopment())
        {
            headers.TryAdd(
                "Content-Security-Policy",
                "default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; script-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; connect-src 'self'; upgrade-insecure-requests");
        }
        if (context.User.Identity?.IsAuthenticated == true)
            headers.TryAdd("Cache-Control", "no-store, max-age=0");
        return Task.CompletedTask;
    });
    await next();
});

// ---- Endpoints ----
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHealthChecks("/health/ml", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ml"),
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHub<CounselorQueueHub>(CounselorQueueHub.Route);
// Peer chat has no server-owned room membership or moderation model yet. It is not
// mapped until those privacy and authorization requirements are implemented.
app.MapHub<AdminNotificationHub>(AdminNotificationHub.Route);
if (useHangfire)
{
    app.MapHangfireDashboard()
        .RequireAuthorization(PolicyConstants.CanAccessAdmin);
}

// ---- Startup work: migrate DB, seed roles, schedule recurring jobs ----
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    if (initializeDatabase)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await DbInitializer.InitializeAsync(context);

        var roleManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
        await RoleSeeder.SeedRolesAsync(roleManager);

        var userManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Lodestone.Domain.Entities.ApplicationUser>>();
        var adminEmail = builder.Configuration["SeedData:AdminEmail"]
            ?? Environment.GetEnvironmentVariable("LODESTONE_ADMIN_EMAIL");
        var adminPassword = builder.Configuration["SeedData:AdminPassword"]
            ?? Environment.GetEnvironmentVariable("LODESTONE_ADMIN_PASSWORD");
        var resetAdminPassword = builder.Environment.IsDevelopment()
            && builder.Configuration.GetValue("SeedData:ResetAdminPassword", false);
        if (resetAdminPassword)
        {
            app.Logger.LogWarning("SeedData:ResetAdminPassword is set — the development admin password and lockout state will be overwritten on this startup.");
        }

        await AdminUserSeeder.SeedAsync(
            userManager,
            roleManager,
            adminEmail,
            adminPassword ?? string.Empty,
            resetAdminPassword);
    }

    var journalNoteMigrator = services.GetRequiredService<JournalNoteProtectionMigrator>();
    if (protectLegacyJournalNotes)
    {
        var protectedJournalNoteCount = await journalNoteMigrator.ProtectLegacyNotesAsync();
        if (protectedJournalNoteCount > 0)
        {
            app.Logger.LogInformation(
                "Protected {JournalNoteCount} legacy journal notes with the configured Data Protection key ring.",
                protectedJournalNoteCount);
        }
    }
    else if (requireProtectedJournalNotes)
    {
        var legacyJournalNoteCount = await journalNoteMigrator.CountLegacyNotesAsync();
        if (legacyJournalNoteCount > 0)
        {
            throw new InvalidOperationException(
                "Legacy plaintext journal notes remain. Enable Startup:ProtectLegacyJournalNotes before serving this database.");
        }
    }

    if (useHangfire)
    {
        var recurringJobs = services.GetRequiredService<IRecurringJobManager>();
        var riskModelStatus = services
            .GetRequiredService<Lodestone.ML.Models.IRiskModelStatusProvider>()
            .Status;
        RecurringJobScheduler.RegisterRecurringJobs(
            recurringJobs,
            builder.Configuration,
            riskModelStatus.IsAvailable);

        if (riskModelStatus.IsEnabled && !riskModelStatus.IsAvailable)
        {
            app.Logger.LogError(
                "Weekly risk scoring was not scheduled because the configured model is unavailable: {Reason}",
                riskModelStatus.UnavailableReason);
        }
    }
}

app.Run();

static async Task WriteHealthResponseAsync(
    HttpContext context,
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
    }, context.RequestAborted);
}

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
