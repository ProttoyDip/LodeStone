using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Lodestone.IntegrationTests.Repositories;

public sealed class RiskPersistenceRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 8, 30, 0, TimeSpan.Zero);
    private static readonly RiskModelDescriptor Descriptor = new(
        "model-v1",
        RiskFeatureSchema.Withdrawal28DayV1,
        RiskFeatureSchema.Withdrawal28DayObservedDays,
        0.50);

    [Fact]
    public async Task PersistAsync_IsIdempotentAndPreservesPeakQueueLevelAndTrigger()
    {
        await using var context = CreateContext();
        var profile = await SeedConsentedStudentAsync(context);
        var firstSnapshot = Snapshot(profile.Id, "COURSE-A", Now.UtcDateTime.AddDays(-1));
        context.RiskFeatureSnapshots.Add(firstSnapshot);
        await context.SaveChangesAsync();
        var repository = new RiskScoreRepository(context, new FixedTimeProvider(Now));

        var first = await repository.PersistAsync(
            firstSnapshot,
            Descriptor,
            0.80,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);
        var duplicate = await repository.PersistAsync(
            firstSnapshot,
            Descriptor,
            0.80,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);

        first.Outcome.Should().Be(RiskScorePersistenceOutcome.Created);
        first.QueueCreated.Should().BeTrue();
        duplicate.Outcome.Should().Be(RiskScorePersistenceOutcome.AlreadyExists);
        (await context.RiskScores.CountAsync()).Should().Be(1);
        (await context.RiskQueueEntries.CountAsync(entry => !entry.IsResolved)).Should().Be(1);

        var secondSnapshot = Snapshot(profile.Id, "COURSE-B", Now.UtcDateTime);
        context.RiskFeatureSnapshots.Add(secondSnapshot);
        await context.SaveChangesAsync();
        var second = await repository.PersistAsync(
            secondSnapshot,
            Descriptor,
            0.60,
            RiskLevel.High,
            Now.UtcDateTime.AddMinutes(5),
            null);

        second.QueueCreated.Should().BeFalse();
        second.QueueEscalated.Should().BeFalse();
        var queue = await context.RiskQueueEntries.AsNoTracking().SingleAsync();
        queue.Level.Should().Be(RiskLevel.Critical);
        queue.TriggerRiskScoreId.Should().Be(first.RiskScore!.Id);
        queue.RiskScoreId.Should().Be(second.RiskScore!.Id);
    }

    [Fact]
    public async Task PersistAsync_ConvergesToAlreadyExistsAfterAConcurrentUniqueKeyCollision()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"risk-concurrency-{Guid.NewGuid()}";
        var options = CreateOptions(databaseName, root);
        int snapshotId;
        int profileId;
        await using (var seed = new ApplicationDbContext(options))
        {
            var profile = await SeedConsentedStudentAsync(seed);
            var snapshot = Snapshot(profile.Id, "COURSE-CONCURRENT", Now.UtcDateTime);
            seed.RiskFeatureSnapshots.Add(snapshot);
            await seed.SaveChangesAsync();
            snapshotId = snapshot.Id;
            profileId = profile.Id;
        }

        await using var racingContext = new RaceApplicationDbContext(options, async cancellationToken =>
        {
            await using var winningContext = new ApplicationDbContext(options);
            var winningSnapshot = await winningContext.RiskFeatureSnapshots.SingleAsync(
                snapshot => snapshot.Id == snapshotId,
                cancellationToken);
            var winningScore = new RiskScore
            {
                StudentProfileId = profileId,
                RiskFeatureSnapshotId = snapshotId,
                CourseKey = winningSnapshot.CourseKey,
                WindowEndUtc = winningSnapshot.WindowEndUtc,
                FeatureSchemaVersion = Descriptor.FeatureSchemaVersion,
                Probability = .80,
                Level = RiskLevel.Critical,
                ScoredAtUtc = Now.UtcDateTime,
                ModelVersion = Descriptor.ModelVersion,
                CreatedAtUtc = Now.UtcDateTime
            };
            winningContext.RiskScores.Add(winningScore);
            winningContext.RiskQueueEntries.Add(new RiskQueueEntry
            {
                StudentProfileId = profileId,
                RiskScore = winningScore,
                TriggerRiskScore = winningScore,
                Level = RiskLevel.Critical,
                LastSignaledAtUtc = Now.UtcDateTime,
                IsResolved = false,
                CreatedAtUtc = Now.UtcDateTime
            });
            await winningContext.SaveChangesAsync(cancellationToken);
            throw new DbUpdateException("A concurrent score won the unique-key race.");
        });

        var repository = new RiskScoreRepository(racingContext, new FixedTimeProvider(Now));
        var result = await repository.PersistAsync(
            new RiskFeatureSnapshot { Id = snapshotId },
            Descriptor,
            .80,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);

        result.Outcome.Should().Be(RiskScorePersistenceOutcome.AlreadyExists);
        result.QueueCreated.Should().BeFalse();
        result.QueueEscalated.Should().BeFalse();
        result.RiskScore.Should().NotBeNull();
        await using var assertionContext = new ApplicationDbContext(options);
        (await assertionContext.RiskScores.CountAsync()).Should().Be(1);
        (await assertionContext.RiskQueueEntries.CountAsync(entry => !entry.IsResolved)).Should().Be(1);
    }

    [Fact]
    public async Task WithdrawConsent_DeletesDerivedDataButPreservesConsentHistory()
    {
        await using var context = CreateContext();
        var profile = await SeedConsentedStudentAsync(context);
        context.ActivityLogs.Add(new ActivityLog
        {
            StudentProfileId = profile.Id,
            OccurredAtUtc = Now.UtcDateTime,
            LoginCount = 1
        });
        var snapshot = Snapshot(profile.Id, "COURSE-A", Now.UtcDateTime);
        context.RiskFeatureSnapshots.Add(snapshot);
        await context.SaveChangesAsync();
        var scoring = new RiskScoreRepository(context, new FixedTimeProvider(Now));
        await scoring.PersistAsync(
            snapshot,
            Descriptor,
            0.80,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);
        var consent = new RiskMonitoringConsentRepository(context, new FixedTimeProvider(Now.AddMinutes(10)));

        var result = await consent.SetByUserIdAsync(profile.UserId, false, profile.UserId);

        result.IsConsented.Should().BeFalse();
        (await context.ActivityLogs.CountAsync()).Should().Be(0);
        (await context.RiskFeatureSnapshots.CountAsync()).Should().Be(0);
        (await context.RiskScores.CountAsync()).Should().Be(0);
        (await context.RiskQueueEntries.CountAsync()).Should().Be(0);
        (await context.RiskMonitoringConsentHistory.CountAsync()).Should().Be(1);
        (await context.RiskMonitoringConsents.SingleAsync()).IsConsented.Should().BeFalse();
    }

    [Fact]
    public async Task ActivityLogRepository_RecordsOnlyForConsentedStudent()
    {
        await using var context = CreateContext();
        var profile = await SeedConsentedStudentAsync(context);
        var repository = new ActivityLogRepository(context);

        var recorded = await repository.RecordLoginIfConsentedAsync(profile.UserId, Now.UtcDateTime);
        context.RiskMonitoringConsents.Single().IsConsented = false;
        await context.SaveChangesAsync();
        var rejected = await repository.RecordLoginIfConsentedAsync(profile.UserId, Now.UtcDateTime.AddMinutes(1));

        recorded.Should().BeTrue();
        rejected.Should().BeFalse();
        (await context.ActivityLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PendingSnapshots_require_an_active_consent_and_verified_student_number_at_scoring_time()
    {
        await using var context = CreateContext();
        var unverified = await SeedConsentedStudentAsync(context, studentNumber: null);
        var snapshot = Snapshot(unverified.Id, "COURSE-UNVERIFIED", Now.UtcDateTime);
        context.RiskFeatureSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        var snapshots = new RiskFeatureSnapshotRepository(context, new FixedTimeProvider(Now));
        var pending = await snapshots.GetPendingIdsAsync(
            Descriptor,
            Now.UtcDateTime,
            RiskScoringPolicy.MaximumSnapshotAgeDays);
        pending.Should().BeEmpty("an unverified identifier must block scoring even for a stale snapshot");

        var scores = new RiskScoreRepository(context, new FixedTimeProvider(Now));
        var persistence = await scores.PersistAsync(
            snapshot,
            Descriptor,
            .8,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);
        persistence.Outcome.Should().Be(RiskScorePersistenceOutcome.NotEligible);
        (await context.RiskScores.CountAsync()).Should().Be(0);
        (await context.RiskQueueEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Consent_withdrawal_prevents_later_scoring_of_an_already_imported_snapshot()
    {
        await using var context = CreateContext();
        var profile = await SeedConsentedStudentAsync(context);
        var snapshot = Snapshot(profile.Id, "COURSE-WITHDRAWN", Now.UtcDateTime);
        context.RiskFeatureSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        var consent = new RiskMonitoringConsentRepository(context, new FixedTimeProvider(Now));
        await consent.SetByUserIdAsync(profile.UserId, false, profile.UserId);

        var snapshots = new RiskFeatureSnapshotRepository(context, new FixedTimeProvider(Now));
        (await snapshots.GetPendingIdsAsync(
            Descriptor,
            Now.UtcDateTime,
            RiskScoringPolicy.MaximumSnapshotAgeDays)).Should().BeEmpty();

        var scores = new RiskScoreRepository(context, new FixedTimeProvider(Now));
        var persistence = await scores.PersistAsync(
            snapshot,
            Descriptor,
            .8,
            RiskLevel.Critical,
            Now.UtcDateTime,
            null);
        persistence.Outcome.Should().Be(RiskScorePersistenceOutcome.NotEligible);
        (await context.RiskScores.CountAsync()).Should().Be(0);
        (await context.RiskQueueEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_TreatsCourseKeysCaseInsensitivelyAndRejectsConflictingFileAtomically()
    {
        await using var context = CreateContext();
        await SeedConsentedStudentAsync(context, "ST-001");
        var repository = new RiskFeatureSnapshotRepository(context, new FixedTimeProvider(Now));
        var rows = new[]
        {
            ImportRow("st-001", "COURSE-A", 0.25f, 2),
            ImportRow("ST-001", "course-a", 0.75f, 3)
        };

        var result = await repository.ImportAsync(
            "snapshots.csv",
            new string('a', 64),
            rows,
            Array.Empty<RiskSnapshotImportErrorDto>(),
            "admin-user");

        result.ImportedRows.Should().Be(0);
        result.RejectedRows.Should().Be(2);
        result.Errors.Should().ContainSingle(error => error.Message.Contains("Conflicting"));
        (await context.RiskFeatureSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_AcceptsTheSeparatelyVersionedV2FeatureSchemaAndExposesItForMatchingModel()
    {
        await using var context = CreateContext();
        var profile = await SeedConsentedStudentAsync(context, "ST-002");
        var repository = new RiskFeatureSnapshotRepository(context, new FixedTimeProvider(Now));
        var row = new RiskFeatureSnapshotImportDto(
            "ST-002", "COURSE-V2", Now.UtcDateTime.AddDays(-1), 28,
            RiskFeatureSchema.Withdrawal28DayV2,
            0, 0, 0, 0, 0, 0,
            SourceRowNumber: 2,
            RecentActiveDayRate: .10f,
            PriorActiveDayRate: .20f,
            ActiveDayRateTrend: -.10f,
            RecentCourseClickRate: 1,
            PriorCourseClickRate: 2,
            CourseClickRateTrend: -1,
            InactivityStreakDays: 10,
            AssessmentDueRate: .05f,
            AssessmentOnTimeRate: .5f,
            AssessmentLateOrMissingRate: .5f,
            CourseProgressRatio: .4f,
            CohortActivityPercentile: .25f);

        var imported = await repository.ImportAsync(
            "v2-snapshots.csv", new string('b', 64), [row], [], "admin-user");

        imported.ImportedRows.Should().Be(1);
        var snapshot = await context.RiskFeatureSnapshots.SingleAsync();
        snapshot.StudentProfileId.Should().Be(profile.Id);
        snapshot.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV2);
        snapshot.RecentActiveDayRate.Should().Be(.10f);
        snapshot.ActiveDayRate.Should().Be(0, "v1 values are never repurposed for v2");

        var pending = await repository.GetPendingIdsAsync(
            new RiskModelDescriptor("model-v2", RiskFeatureSchema.Withdrawal28DayV2, 28, .5),
            Now.UtcDateTime,
            RiskScoringPolicy.MaximumSnapshotAgeDays);
        pending.Should().ContainSingle().Which.Should().Be(snapshot.Id);
    }

    [Fact]
    public async Task QueueResolution_RequiresWellFormedCurrentRowVersion()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = $"risk-queue-{Guid.NewGuid()}";
        await using (var seedContext = CreateContext(databaseName, root))
        {
            var profile = await SeedConsentedStudentAsync(seedContext);
            var snapshot = Snapshot(profile.Id, "COURSE-A", Now.UtcDateTime);
            seedContext.RiskFeatureSnapshots.Add(snapshot);
            await seedContext.SaveChangesAsync();
            var scoring = new RiskScoreRepository(seedContext, new FixedTimeProvider(Now));
            await scoring.PersistAsync(
                snapshot,
                Descriptor,
                0.80,
                RiskLevel.Critical,
                Now.UtcDateTime,
                null);
            var queue = await seedContext.RiskQueueEntries.SingleAsync();
            queue.RowVersion = new byte[] { 1 };
            await seedContext.SaveChangesAsync();
        }

        await using (var invalidContext = CreateContext(databaseName, root))
        {
            var repository = new CounselorQueueRepository(invalidContext, new FixedTimeProvider(Now));
            var queueId = await invalidContext.RiskQueueEntries.Select(entry => entry.Id).SingleAsync();
            (await repository.ResolveAsync(queueId, "counselor", null)).Should()
                .Be(RiskQueueResolutionOutcome.ConcurrencyConflict);
            (await repository.ResolveAsync(queueId, "counselor", "not-base64")).Should()
                .Be(RiskQueueResolutionOutcome.ConcurrencyConflict);
            (await repository.ResolveAsync(queueId, "counselor", Convert.ToBase64String(new byte[] { 0 }))).Should()
                .Be(RiskQueueResolutionOutcome.ConcurrencyConflict);
        }

        await using (var validContext = CreateContext(databaseName, root))
        {
            var repository = new CounselorQueueRepository(validContext, new FixedTimeProvider(Now));
            var queue = await validContext.RiskQueueEntries.SingleAsync();
            var outcome = await repository.ResolveAsync(
                queue.Id,
                "counselor",
                Convert.ToBase64String(queue.RowVersion));

            outcome.Should().Be(RiskQueueResolutionOutcome.Resolved);
            (await validContext.RiskQueueEntries.SingleAsync()).IsResolved.Should().BeTrue();
            (await validContext.AuditLogs.CountAsync(log => log.Action == "RiskQueue.Resolved")).Should().Be(1);
        }
    }

    [Fact]
    public void Model_ContainsRequiredFilteredUniqueIndexesAndConcurrencyToken()
    {
        using var context = CreateContext();
        var studentIndex = context.Model.FindEntityType(typeof(StudentProfile))!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { "StudentNumber" }));
        var queueIndex = context.Model.FindEntityType(typeof(RiskQueueEntry))!.GetIndexes()
            .Single(index => index.GetDatabaseName() == "UX_RiskQueueEntries_OneOpenPerStudent");
        var rowVersion = context.Model.FindEntityType(typeof(RiskQueueEntry))!
            .FindProperty(nameof(RiskQueueEntry.RowVersion));

        studentIndex.IsUnique.Should().BeTrue();
        studentIndex.GetFilter().Should().Be("[StudentNumber] IS NOT NULL");
        queueIndex.IsUnique.Should().BeTrue();
        queueIndex.GetFilter().Should().Be("[IsResolved] = 0");
        rowVersion!.IsConcurrencyToken.Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext(
        string? databaseName = null,
        InMemoryDatabaseRoot? root = null)
    {
        return new ApplicationDbContext(CreateOptions(databaseName, root));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(
        string? databaseName = null,
        InMemoryDatabaseRoot? root = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (root is null)
            builder.UseInMemoryDatabase(databaseName ?? $"risk-tests-{Guid.NewGuid()}");
        else
            builder.UseInMemoryDatabase(databaseName!, root);
        return builder.Options;
    }

    private static async Task<StudentProfile> SeedConsentedStudentAsync(
        ApplicationDbContext context,
        string? studentNumber = "STUDENT-001")
    {
        var user = new ApplicationUser
        {
            Id = $"user-{Guid.NewGuid()}",
            UserName = $"student-{Guid.NewGuid()}@example.test",
            FullName = "Test Student",
            IsActive = true,
            CreatedAtUtc = Now.UtcDateTime
        };
        var profile = new StudentProfile
        {
            User = user,
            UserId = user.Id,
            StudentNumber = studentNumber,
            CreatedAtUtc = Now.UtcDateTime
        };
        profile.RiskMonitoringConsent = new RiskMonitoringConsent
        {
            StudentProfile = profile,
            IsConsented = true,
            PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
            ConsentedAtUtc = Now.UtcDateTime,
            CreatedAtUtc = Now.UtcDateTime
        };
        context.StudentProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static RiskFeatureSnapshot Snapshot(int studentProfileId, string courseKey, DateTime windowEndUtc)
        => new()
        {
            StudentProfileId = studentProfileId,
            CourseKey = courseKey,
            WindowEndUtc = windowEndUtc,
            ObservedDays = RiskFeatureSchema.Withdrawal28DayObservedDays,
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV1,
            SourceFileName = "fixture.csv",
            SourceFileSha256 = new string('a', 64),
            ActiveDayRate = 0.5f,
            ActivitySpanDays = 20,
            DaysSinceLastAccess = 2,
            ForumInteractionCount = 3,
            CourseInteractionCount = 40,
            LateOrMissingAssignmentCount = 1,
            CreatedAtUtc = Now.UtcDateTime
        };

    private static RiskFeatureSnapshotImportDto ImportRow(
        string studentNumber,
        string courseKey,
        float activeDayRate,
        int rowNumber)
        => new(
            studentNumber,
            courseKey,
            Now.UtcDateTime.AddDays(-1),
            RiskFeatureSchema.Withdrawal28DayObservedDays,
            RiskFeatureSchema.Withdrawal28DayV1,
            activeDayRate,
            20,
            2,
            3,
            40,
            1,
            rowNumber);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RaceApplicationDbContext : ApplicationDbContext
    {
        private readonly Func<CancellationToken, Task> _beforeFirstSave;
        private bool _hasInjectedCollision;

        public RaceApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            Func<CancellationToken, Task> beforeFirstSave)
            : base(options)
            => _beforeFirstSave = beforeFirstSave;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_hasInjectedCollision)
            {
                _hasInjectedCollision = true;
                await _beforeFirstSave(cancellationToken);
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
