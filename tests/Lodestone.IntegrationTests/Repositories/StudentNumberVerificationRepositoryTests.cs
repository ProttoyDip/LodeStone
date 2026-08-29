using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lodestone.IntegrationTests.Repositories;

public sealed class StudentNumberVerificationRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitAsync_NormalizesAndAllowsOnlyOnePendingClaim()
    {
        await using var context = CreateContext();
        var profile = await SeedStudentAsync(context);
        var service = CreateService(context);

        var submitted = await service.SubmitAsync(profile.UserId, " stu-001/a ");
        var duplicate = await service.SubmitAsync(profile.UserId, "STU-002");

        submitted.Outcome.Should().Be(StudentNumberClaimOutcome.Submitted);
        submitted.Claim!.ClaimedStudentNumber.Should().Be("STU-001/A");
        submitted.Claim.RowVersionToken.Should().NotBeNullOrWhiteSpace();
        duplicate.Outcome.Should().Be(StudentNumberClaimOutcome.PendingClaimExists);
        (await context.StudentNumberClaims.CountAsync()).Should().Be(1);
        (await context.StudentProfiles.SingleAsync()).StudentNumber.Should().BeNull();
    }

    [Fact]
    public async Task ApproveAsync_RejectsNumberVerifiedOnAnotherProfile()
    {
        await using var context = CreateContext();
        await SeedStudentAsync(context, "DUP-001");
        var claimant = await SeedStudentAsync(context);
        var service = CreateService(context);
        var submitted = await service.SubmitAsync(claimant.UserId, "dup-001");

        var reviewed = await service.ApproveAsync(
            submitted.Claim!.Id,
            "admin-user",
            submitted.Claim.RowVersionToken);

        reviewed.Outcome.Should().Be(StudentNumberClaimOutcome.DuplicateStudentNumber);
        (await context.StudentProfiles.FindAsync(claimant.Id))!.StudentNumber.Should().BeNull();
        (await context.StudentNumberClaims.FindAsync(submitted.Claim.Id))!.Status.Should()
            .Be(StudentNumberClaimStatus.Pending);
        (await context.AuditLogs.CountAsync(log => log.Action == "StudentNumberClaim.Approved"))
            .Should().Be(0);
    }

    [Fact]
    public async Task ApproveAsync_RequiresCurrentRowVersionAndAssignsOnlyAfterApproval()
    {
        await using var context = CreateContext();
        var profile = await SeedStudentAsync(context);
        var service = CreateService(context);
        var submitted = await service.SubmitAsync(profile.UserId, "verified-42");

        var stale = await service.ApproveAsync(
            submitted.Claim!.Id,
            "admin-user",
            Convert.ToBase64String(new byte[] { 99 }));
        var approved = await service.ApproveAsync(
            submitted.Claim.Id,
            "admin-user",
            submitted.Claim.RowVersionToken);

        stale.Outcome.Should().Be(StudentNumberClaimOutcome.ConcurrencyConflict);
        approved.Outcome.Should().Be(StudentNumberClaimOutcome.Approved);
        approved.State!.VerifiedStudentNumber.Should().Be("VERIFIED-42");
        var persisted = await context.StudentNumberClaims.FindAsync(submitted.Claim.Id);
        persisted!.Status.Should().Be(StudentNumberClaimStatus.Approved);
        persisted.ReviewedByUserId.Should().Be("admin-user");
        (await context.StudentProfiles.FindAsync(profile.Id))!.StudentNumber.Should().Be("VERIFIED-42");
        (await context.AuditLogs.CountAsync(log => log.Action == "StudentNumberClaim.Approved"))
            .Should().Be(1);
    }

    [Fact]
    public async Task RejectAsync_LeavesMappingEmptyAndAllowsResubmission()
    {
        await using var context = CreateContext();
        var profile = await SeedStudentAsync(context);
        var service = CreateService(context);
        var first = await service.SubmitAsync(profile.UserId, "WRONG-001");

        var rejected = await service.RejectAsync(
            first.Claim!.Id,
            "admin-user",
            first.Claim.RowVersionToken);
        var second = await service.SubmitAsync(profile.UserId, "RIGHT-001");

        rejected.Outcome.Should().Be(StudentNumberClaimOutcome.Rejected);
        second.Outcome.Should().Be(StudentNumberClaimOutcome.Submitted);
        (await context.StudentProfiles.FindAsync(profile.Id))!.StudentNumber.Should().BeNull();
        (await context.StudentNumberClaims.CountAsync()).Should().Be(2);
        (await context.StudentNumberClaims.CountAsync(
            claim => claim.Status == StudentNumberClaimStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task ResetAsync_DisablesConsentPurgesDerivedDataAndRequiresNewClaim()
    {
        await using var context = CreateContext();
        var profile = await SeedStudentAsync(context, "VERIFIED-001", consented: true);
        context.StudentNumberClaims.Add(new StudentNumberClaim
        {
            StudentProfileId = profile.Id,
            ClaimedStudentNumber = "VERIFIED-001",
            Status = StudentNumberClaimStatus.Approved,
            SubmittedAtUtc = Now.UtcDateTime.AddDays(-1),
            ReviewedAtUtc = Now.UtcDateTime.AddHours(-20),
            ReviewedByUserId = "first-admin",
            RowVersion = new byte[] { 1 },
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1)
        });
        context.ActivityLogs.Add(new ActivityLog
        {
            StudentProfileId = profile.Id,
            OccurredAtUtc = Now.UtcDateTime,
            LoginCount = 1
        });
        var snapshot = Snapshot(profile.Id);
        context.RiskFeatureSnapshots.Add(snapshot);
        await context.SaveChangesAsync();
        var score = new RiskScore
        {
            StudentProfileId = profile.Id,
            RiskFeatureSnapshotId = snapshot.Id,
            CourseKey = snapshot.CourseKey,
            WindowEndUtc = snapshot.WindowEndUtc,
            FeatureSchemaVersion = snapshot.FeatureSchemaVersion,
            Probability = 0.9,
            Level = RiskLevel.Critical,
            ScoredAtUtc = Now.UtcDateTime,
            ModelVersion = "model-v1",
            CreatedAtUtc = Now.UtcDateTime
        };
        context.RiskScores.Add(score);
        await context.SaveChangesAsync();
        context.RiskQueueEntries.Add(new RiskQueueEntry
        {
            StudentProfileId = profile.Id,
            RiskScoreId = score.Id,
            TriggerRiskScoreId = score.Id,
            Level = RiskLevel.Critical,
            LastSignaledAtUtc = Now.UtcDateTime,
            CreatedAtUtc = Now.UtcDateTime,
            RowVersion = new byte[] { 2 }
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var reset = await service.ResetAsync(profile.Id, "reset-admin");
        var resubmitted = await service.SubmitAsync(profile.UserId, "NEW-001");

        reset.Outcome.Should().Be(StudentNumberClaimOutcome.Reset);
        resubmitted.Outcome.Should().Be(StudentNumberClaimOutcome.Submitted);
        (await context.StudentProfiles.FindAsync(profile.Id))!.StudentNumber.Should().BeNull();
        (await context.RiskMonitoringConsents.SingleAsync()).IsConsented.Should().BeFalse();
        (await context.RiskMonitoringConsentHistory.CountAsync()).Should().Be(1);
        (await context.ActivityLogs.CountAsync()).Should().Be(0);
        (await context.RiskFeatureSnapshots.CountAsync()).Should().Be(0);
        (await context.RiskScores.CountAsync()).Should().Be(0);
        (await context.RiskQueueEntries.CountAsync()).Should().Be(0);
        (await context.AuditLogs.CountAsync(log => log.Action == "StudentNumber.Reset"))
            .Should().Be(1);
    }

    [Fact]
    public void Model_ConfiguresPendingUniquenessLengthsAndConcurrency()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(StudentNumberClaim))!;
        var pendingIndex = entity.GetIndexes()
            .Single(index => index.GetDatabaseName() == "UX_StudentNumberClaims_OnePendingPerStudent");

        pendingIndex.IsUnique.Should().BeTrue();
        pendingIndex.GetFilter().Should().Be("[Status] = 0");
        entity.FindProperty(nameof(StudentNumberClaim.ClaimedStudentNumber))!.GetMaxLength()
            .Should().Be(64);
        entity.FindProperty(nameof(StudentNumberClaim.ReviewedByUserId))!.GetMaxLength()
            .Should().Be(450);
        entity.FindProperty(nameof(StudentNumberClaim.RowVersion))!.IsConcurrencyToken
            .Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"student-number-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static StudentNumberVerificationService CreateService(ApplicationDbContext context)
        => new(new StudentNumberVerificationRepository(context, new FixedTimeProvider(Now)));

    private static async Task<StudentProfile> SeedStudentAsync(
        ApplicationDbContext context,
        string? verifiedStudentNumber = null,
        bool consented = false)
    {
        var user = new ApplicationUser
        {
            Id = $"student-user-{Guid.NewGuid()}",
            UserName = $"student-{Guid.NewGuid()}@example.test",
            Email = $"student-{Guid.NewGuid()}@example.test",
            FullName = "Test Student",
            IsActive = true,
            CreatedAtUtc = Now.UtcDateTime
        };
        var profile = new StudentProfile
        {
            User = user,
            UserId = user.Id,
            StudentNumber = verifiedStudentNumber,
            CreatedAtUtc = Now.UtcDateTime
        };
        if (consented)
        {
            profile.RiskMonitoringConsent = new RiskMonitoringConsent
            {
                StudentProfile = profile,
                IsConsented = true,
                PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
                ConsentedAtUtc = Now.UtcDateTime,
                CreatedAtUtc = Now.UtcDateTime
            };
        }

        context.StudentProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static RiskFeatureSnapshot Snapshot(int studentProfileId)
        => new()
        {
            StudentProfileId = studentProfileId,
            CourseKey = "COURSE-A",
            WindowEndUtc = Now.UtcDateTime.AddDays(-1),
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
