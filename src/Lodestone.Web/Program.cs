using Hangfire;
using Lodestone.Application;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Infrastructure;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Identity;
using Lodestone.Jobs;
using Lodestone.Jobs.Scheduling;
using Lodestone.ML;
using Lodestone.Reporting;
using Lodestone.Web;
using Lodestone.Web.Health;
using Lodestone.Web.Hubs;
using Lodestone.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var useHangfire = builder.Configuration.GetValue("Startup:UseHangfire", true);
var initializeDatabase = builder.Configuration.GetValue("Startup:InitializeDatabase", true);

// QuestPDF community licence (report generation lives in Lodestone.Reporting).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// ---- MVC + Web-only services ----
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 5001);
}
var mvcBuilder = builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // Identity UI area
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHealthChecks()
    .AddCheck<RiskModelHealthCheck>("risk-model", tags: ["ready", "ml"]);

// ---- Authorization policies (defined in Infrastructure/Identity) ----
builder.Services.AddAuthorization(IdentityPolicySeeder.AddPolicies);

// ---- Clean Architecture layer registrations ----
builder.Services.AddApplication();
// Override Application's transport-neutral no-op with Web's authorized SignalR refresh signal.
builder.Services.AddScoped<IRiskQueueNotifier, SignalRRiskQueueNotifier>();
builder.Services.AddInfrastructure(builder.Configuration);
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

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// ---- Endpoints ----
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapHealthChecks("/health/ml", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ml"),
    ResponseWriter = static async (context, report) =>
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
});
app.MapHub<CounselorQueueHub>(CounselorQueueHub.Route);
app.MapHub<PeerChatHub>(PeerChatHub.Route);
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
            adminPassword ?? string.Empty,
            resetAdminPassword);
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

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
