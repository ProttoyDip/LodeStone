using FluentAssertions;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class StudentNumberVerificationServiceTests
{
    [Fact]
    public async Task SubmitAsync_TrimsAndNormalizesValidStudentNumber()
    {
        var repository = new Mock<IStudentNumberVerificationRepository>();
        repository.Setup(value => value.SubmitAsync(
                "student-user",
                "STU_1/A.2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentNumberClaimResultDto(StudentNumberClaimOutcome.Submitted));
        var service = new StudentNumberVerificationService(repository.Object);

        var result = await service.SubmitAsync(" student-user ", " stu_1/a.2 ");

        result.Outcome.Should().Be(StudentNumberClaimOutcome.Submitted);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-STUDENT")]
    [InlineData("STUDENT NUMBER")]
    [InlineData("STUDENT@1")]
    [InlineData("STUDÉNT")]
    public async Task SubmitAsync_RejectsStudentNumbersOutsideImportContract(string value)
    {
        var repository = new Mock<IStudentNumberVerificationRepository>(MockBehavior.Strict);
        var service = new StudentNumberVerificationService(repository.Object);

        var result = await service.SubmitAsync("student-user", value);

        result.Outcome.Should().Be(StudentNumberClaimOutcome.InvalidStudentNumber);
    }

    [Fact]
    public async Task SubmitAsync_RejectsStudentNumberLongerThan64Characters()
    {
        var repository = new Mock<IStudentNumberVerificationRepository>(MockBehavior.Strict);
        var service = new StudentNumberVerificationService(repository.Object);

        var result = await service.SubmitAsync("student-user", new string('A', 65));

        result.Outcome.Should().Be(StudentNumberClaimOutcome.InvalidStudentNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task ApproveAsync_ReturnsConcurrencyConflictForInvalidRowVersion(string token)
    {
        var repository = new Mock<IStudentNumberVerificationRepository>(MockBehavior.Strict);
        var service = new StudentNumberVerificationService(repository.Object);

        var result = await service.ApproveAsync(7, "admin-user", token);

        result.Outcome.Should().Be(StudentNumberClaimOutcome.ConcurrencyConflict);
    }

    [Fact]
    public async Task ResetAsync_RejectsInvalidIdentifiersWithoutCallingRepository()
    {
        var repository = new Mock<IStudentNumberVerificationRepository>(MockBehavior.Strict);
        var service = new StudentNumberVerificationService(repository.Object);

        var result = await service.ResetAsync(0, "admin-user");

        result.Outcome.Should().Be(StudentNumberClaimOutcome.InvalidRequest);
    }
}
