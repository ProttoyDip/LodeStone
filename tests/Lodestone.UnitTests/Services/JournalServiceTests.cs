using FluentAssertions;
using Lodestone.Application.DTOs.Journal;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Services;

public class JournalServiceTests
{
    [Fact]
    public async Task GetEntriesAsync_MapsRepositoryResultsInRepositoryOrder()
    {
        var entries = new List<MoodJournalEntry>
        {
            new() { Id = 2, StudentProfileId = 7, MoodRating = 4, Note = "protected:Later", EntryDateUtc = new DateTime(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc) },
            new() { Id = 1, StudentProfileId = 7, MoodRating = 2, Note = "protected:Earlier", EntryDateUtc = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc) }
        };
        var repository = new Mock<IJournalRepository>();
        repository.Setup(value => value.GetByStudentIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        var service = new JournalService(repository.Object, Mock.Of<IUnitOfWork>(), new StubSensitiveDataProtector());

        var result = await service.GetEntriesAsync(7);

        result.Select(entry => entry.Id).Should().ContainInOrder(2, 1);
        result.Should().OnlyContain(entry => entry.StudentProfileId == 7);
        result.Select(entry => entry.Note).Should().ContainInOrder("Later", "Earlier");
    }

    [Fact]
    public async Task AddEntryAsync_TrimsNoteAndPersistsEntry()
    {
        MoodJournalEntry? captured = null;
        var repository = new Mock<IJournalRepository>();
        repository.Setup(value => value.HasEntryForDayAsync(7, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository.Setup(value => value.AddAsync(It.IsAny<MoodJournalEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MoodJournalEntry, CancellationToken>((entry, _) =>
            {
                entry.Id = 42;
                captured = entry;
            })
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new JournalService(repository.Object, unitOfWork.Object, new StubSensitiveDataProtector());

        var result = await service.AddEntryAsync(7, new CreateJournalEntryDto(4, "  A better day.  "));

        captured.Should().NotBeNull();
        captured!.StudentProfileId.Should().Be(7);
        captured.MoodRating.Should().Be(4);
        captured.Note.Should().Be("protected:A better day.");
        captured.NoteProtectionVersion.Should().Be(1);
        captured.EntryDateUtc.Kind.Should().Be(DateTimeKind.Utc);
        result.Id.Should().Be(42);
        result.Note.Should().Be("A better day.");
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddEntryAsync_ConvertsWhitespaceNoteToNull()
    {
        MoodJournalEntry? captured = null;
        var repository = new Mock<IJournalRepository>();
        repository.Setup(value => value.HasEntryForDayAsync(7, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository.Setup(value => value.AddAsync(It.IsAny<MoodJournalEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MoodJournalEntry, CancellationToken>((entry, _) => captured = entry)
            .Returns(Task.CompletedTask);
        var service = new JournalService(repository.Object, Mock.Of<IUnitOfWork>(), new StubSensitiveDataProtector());

        await service.AddEntryAsync(7, new CreateJournalEntryDto(3, "   "));

        captured!.Note.Should().BeNull();
    }

    [Fact]
    public async Task AddEntryAsync_RejectsSecondEntryOnTheSameUtcDay()
    {
        var repository = new Mock<IJournalRepository>();
        repository.Setup(value => value.HasEntryForDayAsync(7, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new JournalService(repository.Object, unitOfWork.Object, new StubSensitiveDataProtector());

        var action = () => service.AddEntryAsync(7, new CreateJournalEntryDto(3, null));

        await action.Should().ThrowAsync<DailyJournalEntryLimitException>();
        repository.Verify(value => value.AddAsync(It.IsAny<MoodJournalEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task AddEntryAsync_RejectsInvalidMoodRating(int rating)
    {
        var service = new JournalService(
            Mock.Of<IJournalRepository>(),
            Mock.Of<IUnitOfWork>(),
            new StubSensitiveDataProtector());

        var action = () => service.AddEntryAsync(7, new CreateJournalEntryDto(rating, null));

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private sealed class StubSensitiveDataProtector : ISensitiveDataProtector
    {
        private const string Prefix = "protected:";

        public bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

        public string Protect(string plaintext) => Prefix + plaintext;

        public string Unprotect(string protectedOrLegacyValue)
            => IsProtected(protectedOrLegacyValue)
                ? protectedOrLegacyValue[Prefix.Length..]
                : protectedOrLegacyValue;
    }
}
