using System.Text;
using FluentAssertions;
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
}
