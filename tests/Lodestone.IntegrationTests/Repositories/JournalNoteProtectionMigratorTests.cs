using FluentAssertions;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lodestone.IntegrationTests.Repositories;

public sealed class JournalNoteProtectionMigratorTests
{
    [Fact]
    public async Task ProtectLegacyNotesAsync_protects_active_and_deleted_plaintext_once()
    {
        await using var context = CreateContext();
        var protector = new DataProtectionService(new EphemeralDataProtectionProvider());
        var alreadyProtected = protector.Protect("Already protected");
        context.MoodJournalEntries.AddRange(
            Entry(1, "Active plaintext"),
            Entry(2, "Deleted plaintext", isDeleted: true),
            Entry(3, alreadyProtected, noteProtectionVersion: 1),
            Entry(4, null));
        await context.SaveChangesAsync();
        var migrator = new JournalNoteProtectionMigrator(context, protector);

        var firstCount = await migrator.ProtectLegacyNotesAsync();
        var secondCount = await migrator.ProtectLegacyNotesAsync();

        firstCount.Should().Be(2);
        secondCount.Should().Be(0);
        var stored = await context.MoodJournalEntries.OrderBy(entry => entry.Id).ToListAsync();
        stored[0].Note.Should().NotBe("Active plaintext");
        stored[1].Note.Should().NotBe("Deleted plaintext");
        protector.Unprotect(stored[0].Note!).Should().Be("Active plaintext");
        protector.Unprotect(stored[1].Note!).Should().Be("Deleted plaintext");
        stored[2].Note.Should().Be(alreadyProtected);
        stored.All(entry => entry.Note is null || entry.NoteProtectionVersion == 1).Should().BeTrue();
        stored[3].Note.Should().BeNull();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"journal-protection-tests-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static MoodJournalEntry Entry(
        int id,
        string? note,
        bool isDeleted = false,
        int noteProtectionVersion = 0)
        => new()
        {
            Id = id,
            StudentProfileId = 7,
            MoodRating = 3,
            Note = note,
            NoteProtectionVersion = noteProtectionVersion,
            EntryDateUtc = new DateTime(2026, 8, 29, 12, id, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 8, 29, 12, id, 0, DateTimeKind.Utc),
            IsDeleted = isDeleted
        };
}
