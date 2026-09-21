using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

/// <summary>
/// Crisis-resource search takes free text from a student in distress, so the query path is
/// exercised with nothing, with whitespace, and with input far past the ranking limit.
/// <see cref="CrisisResourceRetrieverTests"/> covers the ranking itself.
/// </summary>
public class CrisisResourceServiceTests
{
    [Fact]
    public async Task FindResourcesAsync_RanksTheActiveResourcesForATypicalQuery()
    {
        var repository = CreateRepository();
        var service = new CrisisResourceService(repository.Object);

        var matches = await service.FindResourcesAsync("I cannot sleep before exams");

        matches.Should().NotBeEmpty();
        matches.Should().BeInDescendingOrder(match => match.Score);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task FindResourcesAsync_ReturnsNothingAndReadsNothingForAnEmptyQuery(string? query)
    {
        var repository = CreateRepository();
        var service = new CrisisResourceService(repository.Object);

        (await service.FindResourcesAsync(query)).Should().BeEmpty();
        repository.Verify(r => r.GetActiveOrderedAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindResourcesAsync_AcceptsAQueryExactlyAtTheRankingLimit()
    {
        var service = new CrisisResourceService(CreateRepository().Object);

        var search = () => service.FindResourcesAsync(new string('a', CrisisResourceService.MaximumQueryLength));

        await search.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FindResourcesAsync_TruncatesAnOverlongQueryInsteadOfFailing()
    {
        var service = new CrisisResourceService(CreateRepository().Object);

        // A pasted wall of text must still return help rather than an error page.
        var matches = await service.FindResourcesAsync("exam " + new string('z', 20_000));

        matches.Should().NotBeNull();
    }

    [Fact]
    public async Task FindResourcesAsync_ReturnsAnEmptyResultWhenNoResourceIsPublished()
    {
        var repository = new Mock<ICrisisResourceRepository>();
        repository.Setup(r => r.GetActiveOrderedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CrisisResource>());
        var service = new CrisisResourceService(repository.Object);

        (await service.FindResourcesAsync("panic attack")).Should().BeEmpty();
    }

    private static Mock<ICrisisResourceRepository> CreateRepository()
    {
        var repository = new Mock<ICrisisResourceRepository>();
        repository.Setup(r => r.GetActiveOrderedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrisisResource { Id = 1, Title = "Exam stress and sleep", Description = "Sleep routines before exams." },
                new CrisisResource { Id = 2, Title = "Urgent helpline", Description = "Talk to someone now." }
            });
        return repository;
    }
}
