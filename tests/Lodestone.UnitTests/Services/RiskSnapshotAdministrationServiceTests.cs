using System.Text;
using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class RiskSnapshotAdministrationServiceTests
{
    [Fact]
    public async Task ImportCsvAsync_RejectsDuplicateHeaderNamesBeforeAnyPersistence()
    {
        var snapshots = new Mock<IRiskFeatureSnapshotRepository>();
        var service = new RiskSnapshotAdministrationService(
            snapshots.Object,
            Mock.Of<IRiskScoringRepository>(),
            Mock.Of<IRiskScoringService>(),
            Mock.Of<IRiskModelPredictor>(),
            TimeProvider.System);
        const string csv =
            "StudentNumber,StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,ActiveDayRate,ActivitySpanDays,DaysSinceLastAccess,ForumInteractionCount,CourseInteractionCount,LateOrMissingAssignmentCount\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await service.ImportCsvAsync(stream, "duplicate-columns.csv", "admin-user");

        result.ImportedRows.Should().Be(0);
        result.RejectedRows.Should().Be(1);
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("duplicate column names");
        snapshots.Verify(repository => repository.ImportAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<Lodestone.Application.DTOs.Risk.RiskFeatureSnapshotImportDto>>(),
            It.IsAny<IReadOnlyList<Lodestone.Application.DTOs.Risk.RiskSnapshotImportErrorDto>>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportCsvAsync_AcceptsAV3FileAndCarriesItsFiveExtraFeatures()
    {
        var snapshots = new Mock<IRiskFeatureSnapshotRepository>();
        IReadOnlyList<RiskFeatureSnapshotImportDto>? captured = null;
        snapshots.Setup(repository => repository.ImportAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RiskFeatureSnapshotImportDto>>(),
                It.IsAny<IReadOnlyList<RiskSnapshotImportErrorDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyList<RiskFeatureSnapshotImportDto> rows,
                       IReadOnlyList<RiskSnapshotImportErrorDto> _, string _, CancellationToken _) => captured = rows)
            .ReturnsAsync(new RiskSnapshotImportResultDto("v3.csv", 1, 1, 0, 0, Array.Empty<RiskSnapshotImportErrorDto>()));

        var service = CreateService(snapshots);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(V3Csv()));

        await service.ImportCsvAsync(stream, "v3.csv", "admin-user");

        captured.Should().NotBeNull();
        var row = captured!.Single();
        row.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV3);
        row.ActivityTrendAcceleration.Should().BeApproximately(-0.25f, 0.0001f);
        row.ClickVolatility.Should().BeApproximately(3.5f, 0.0001f);
        row.ForumEngagementShare.Should().BeApproximately(0.4f, 0.0001f);
        row.InactiveWeekRate.Should().BeApproximately(0.25f, 0.0001f);
        row.AssessmentMissStreak.Should().BeApproximately(2f, 0.0001f);
        row.GetFeatureValues().Should().HaveCount(17);
    }

    [Fact]
    public async Task ImportCsvAsync_StillAcceptsAV2FileWithoutTheV3Columns()
    {
        var snapshots = new Mock<IRiskFeatureSnapshotRepository>();
        IReadOnlyList<RiskFeatureSnapshotImportDto>? captured = null;
        snapshots.Setup(repository => repository.ImportAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RiskFeatureSnapshotImportDto>>(),
                It.IsAny<IReadOnlyList<RiskSnapshotImportErrorDto>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyList<RiskFeatureSnapshotImportDto> rows,
                       IReadOnlyList<RiskSnapshotImportErrorDto> _, string _, CancellationToken _) => captured = rows)
            .ReturnsAsync(new RiskSnapshotImportResultDto("v2.csv", 1, 1, 0, 0, Array.Empty<RiskSnapshotImportErrorDto>()));

        var service = CreateService(snapshots);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(V2Csv()));

        await service.ImportCsvAsync(stream, "v2.csv", "admin-user");

        // Adding v3 must not change how an existing v2 file is read.
        var row = captured!.Single();
        row.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV2);
        row.GetFeatureValues().Should().HaveCount(12);
        row.ActivityTrendAcceleration.Should().BeNull();
    }

    private static RiskSnapshotAdministrationService CreateService(Mock<IRiskFeatureSnapshotRepository> snapshots)
        => new(
            snapshots.Object,
            Mock.Of<IRiskScoringRepository>(),
            Mock.Of<IRiskScoringService>(),
            Mock.Of<IRiskModelPredictor>(),
            TimeProvider.System);

    private const string BaseHeaders = "StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,";
    private const string V2Features =
        "RecentActiveDayRate,PriorActiveDayRate,ActiveDayRateTrend,RecentCourseClickRate,PriorCourseClickRate,"
        + "CourseClickRateTrend,InactivityStreakDays,AssessmentDueRate,AssessmentOnTimeRate,"
        + "AssessmentLateOrMissingRate,CourseProgressRatio,CohortActivityPercentile";
    private const string V2Values = "0.5,0.6,-0.1,4,5,-1,3,0.1,0.8,0.2,0.5,0.4";

    private const string V3Features =
        ",ActivityTrendAcceleration,ClickVolatility,ForumEngagementShare,InactiveWeekRate,AssessmentMissStreak";

    private static string V2Csv()
        => BaseHeaders + V2Features + "\n"
           + "S1,AAA/2026J,2026-09-01T00:00:00Z,28,withdrawal-28d-v2," + V2Values + "\n";

    private static string V3Csv()
        => BaseHeaders + V2Features + V3Features + "\n"
           + "S1,AAA/2026J,2026-09-01T00:00:00Z,28,withdrawal-28d-v3," + V2Values + ",-0.25,3.5,0.4,0.25,2\n";
}
