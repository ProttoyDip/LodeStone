using System.Text;
using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.ML;
using Xunit;

namespace Lodestone.IntegrationTests.Web;

/// <summary>
/// Exercises the supported production path: a quality-gated artifact is loaded by the Web
/// composition root, a verified/consented snapshot is imported, and Application/Infrastructure
/// persist its score and counselor queue case.  It intentionally uses a generated test artifact
/// rather than a synthetic runtime fallback.
/// </summary>
public sealed class RuntimeMlWebPathTests
{
    [Fact]
    public async Task Enabled_missing_artifact_is_unhealthy_and_cannot_start_scoring()
    {
        using var artifactDirectory = new TemporaryDirectory();
        var missingModelPath = Path.Combine(artifactDirectory.Path, "risk-model.zip");
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Startup__InitializeDatabase"] = "false",
            ["Startup__UseHangfire"] = "false",
            ["Startup__ProtectLegacyJournalNotes"] = "true",
            ["MachineLearning__Enabled"] = "true",
            ["MachineLearning__ModelPath"] = missingModelPath,
            ["MachineLearning__MetadataPath"] = Path.ChangeExtension(missingModelPath, ".metadata.json"),
            ["Encryption__KeyRingPath"] = Path.Combine(artifactDirectory.Path, "keys"),
            ["Encryption__ApplicationName"] = "Lodestone.RuntimeMlMissingArtifactTests"
        });
        using var factory = new RuntimeMlWebApplicationFactory($"runtime-web-missing-{Guid.NewGuid():N}");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var modelHealth = await client.GetAsync("/health/ml");
        var readyHealth = await client.GetAsync("/health/ready");

        modelHealth.IsSuccessStatusCode.Should().BeFalse();
        readyHealth.IsSuccessStatusCode.Should().BeFalse();
        (await modelHealth.Content.ReadAsStringAsync()).Should().Contain("Unhealthy");
        (await readyHealth.Content.ReadAsStringAsync()).Should().Contain("Unhealthy");

        await using var scope = factory.Services.CreateAsyncScope();
        var status = scope.ServiceProvider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeFalse();
        var operations = scope.ServiceProvider.GetRequiredService<IRiskSnapshotAdministrationService>();
        var run = () => operations.RunNowAsync("admin-runtime");
        await run.Should().ThrowAsync<InvalidOperationException>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.RiskScores.CountAsync()).Should().Be(0);
        (await context.RiskQueueEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Validated_model_runs_through_web_import_scoring_and_queue_path()
    {
        using var dataset = TestOuladDataset.CreateSeparableTrainingData();
        using var artifactDirectory = new TemporaryDirectory();
        var modelPath = Path.Combine(artifactDirectory.Path, "risk-model.zip");
        var trained = CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = dataset.Path,
            ModelOutputPath = modelPath,
            ModelVersion = "web-runtime-fixture-v1"
        });
        trained.Metadata.EligibleForRuntimeIntegration.Should().BeTrue();
        trained.Report.QualityGate.Passed.Should().BeTrue();

        var databaseName = $"runtime-web-ml-{Guid.NewGuid():N}";
        using var environment = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["Startup__InitializeDatabase"] = "false",
            ["Startup__UseHangfire"] = "false",
            ["Startup__ProtectLegacyJournalNotes"] = "true",
            ["MachineLearning__Enabled"] = "true",
            ["MachineLearning__ModelPath"] = modelPath,
            ["MachineLearning__MetadataPath"] = Path.ChangeExtension(modelPath, ".metadata.json"),
            ["Encryption__KeyRingPath"] = Path.Combine(artifactDirectory.Path, "keys"),
            ["Encryption__ApplicationName"] = "Lodestone.RuntimeMlWebPathTests"
        });
        using var factory = new RuntimeMlWebApplicationFactory(databaseName);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var health = await client.GetAsync("/health/ml");
        health.IsSuccessStatusCode.Should().BeTrue();
        var healthPayload = await health.Content.ReadAsStringAsync();
        healthPayload.Should().Contain("Healthy").And.Contain("web-runtime-fixture-v1");

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = "runtime-web-student",
                UserName = "runtime-web-student@example.test",
                Email = "runtime-web-student@example.test",
                FullName = "Runtime Web Student",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var student = new StudentProfile
            {
                User = user,
                UserId = user.Id,
                StudentNumber = "RUNTIME-001",
                CreatedAtUtc = DateTime.UtcNow
            };
            student.RiskMonitoringConsent = new RiskMonitoringConsent
            {
                StudentProfile = student,
                IsConsented = true,
                PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
                ConsentedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.StudentProfiles.Add(student);
            await context.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var status = scope.ServiceProvider.GetRequiredService<IRiskModelStatusProvider>().Status;
            status.IsAvailable.Should().BeTrue();
            status.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV1);

            var operations = scope.ServiceProvider.GetRequiredService<IRiskSnapshotAdministrationService>();
            var windowEndUtc = DateTime.UtcNow.AddDays(-1);
            var csv = string.Join('\n',
                "StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,ActiveDayRate,ActivitySpanDays,DaysSinceLastAccess,ForumInteractionCount,CourseInteractionCount,LateOrMissingAssignmentCount",
                $"RUNTIME-001,COURSE-RUNTIME,{windowEndUtc:O},28,withdrawal-28d-v1,0,0,28,0,0,1");
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

            var imported = await operations.ImportCsvAsync(stream, "runtime-fixture.csv", "admin-runtime");
            imported.ImportedRows.Should().Be(1);
            imported.Errors.Should().BeEmpty();

            var run = await operations.RunNowAsync("admin-runtime");
            run.ScoredCount.Should().Be(1);
            run.FailedCount.Should().Be(0);

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var score = await context.RiskScores.SingleAsync();
            score.ModelVersion.Should().Be("web-runtime-fixture-v1");
            score.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV1);
            score.Probability.Should().BeInRange(0d, 1d);
            (await context.RiskQueueEntries.CountAsync(entry => !entry.IsResolved)).Should().Be(1);
        }
    }

    private static TrainingPipeline CreatePipeline()
    {
        var mlContext = new MLContext(seed: 42);
        return new TrainingPipeline(
            mlContext,
            new OuladDataLoader(mlContext),
            new FeatureEngineering(mlContext),
            new ModelTrainer(mlContext),
            new ModelEvaluator(mlContext));
    }

    private sealed class RuntimeMlWebApplicationFactory(string databaseName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
            });
        }
    }

    private sealed class TestOuladDataset : IDisposable
    {
        private TestOuladDataset(string path) => Path = path;

        public string Path { get; }

        public static TestOuladDataset CreateSeparableTrainingData()
        {
            var dataset = new TestOuladDataset(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"lodestone-runtime-oulad-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(dataset.Path);
            Write(dataset, "courses.csv", "code_module,code_presentation,module_presentation_length\nAAA,2014J,70\n");
            Write(dataset, "assessments.csv", "code_module,code_presentation,id_assessment,date\nAAA,2014J,1,20\n");
            Write(dataset, "vle.csv", "id_site,code_module,code_presentation,activity_type\n1,AAA,2014J,forumng\n2,AAA,2014J,resource\n");

            var students = new List<string> { "code_module,code_presentation,id_student,final_result" };
            var registrations = new List<string> { "code_module,code_presentation,id_student,date_registration,date_unregistration" };
            var assessments = new List<string> { "id_assessment,id_student,date_submitted,is_banked" };
            var activity = new List<string> { "code_module,code_presentation,id_student,id_site,date,sum_click" };
            for (var student = 1; student <= 30; student++)
            {
                var withdrawn = student <= 15;
                students.Add($"AAA,2014J,{student},{(withdrawn ? "Withdrawn" : "Pass")}");
                registrations.Add($"AAA,2014J,{student},0,{(withdrawn ? "55" : string.Empty)}");
                if (!withdrawn)
                {
                    assessments.Add($"1,{student},18,0");
                    foreach (var day in new[] { 0, 7, 14, 21, 27, 34, 41 })
                    {
                        activity.Add($"AAA,2014J,{student},1,{day},2");
                        activity.Add($"AAA,2014J,{student},2,{day},8");
                    }
                }
            }

            Write(dataset, "studentInfo.csv", string.Join('\n', students) + '\n');
            Write(dataset, "studentRegistration.csv", string.Join('\n', registrations) + '\n');
            Write(dataset, "studentAssessment.csv", string.Join('\n', assessments) + '\n');
            Write(dataset, "studentVle.csv", string.Join('\n', activity) + '\n');
            return dataset;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }

        private static void Write(TestOuladDataset dataset, string fileName, string content)
            => File.WriteAllText(System.IO.Path.Combine(dataset.Path, fileName), content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lodestone-runtime-artifact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string?> _previous;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            _previous = values.Keys.ToDictionary(
                key => key,
                key => Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process),
                StringComparer.Ordinal);
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
        }
    }
}
