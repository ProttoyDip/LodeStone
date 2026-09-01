using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Security;

/// <summary>Protects legacy plaintext journal notes after schema migration and before serving requests.</summary>
public sealed class JournalNoteProtectionMigrator
{
    private const int BatchSize = 100;
    private readonly ApplicationDbContext _context;
    private readonly ISensitiveDataProtector _protector;

    public JournalNoteProtectionMigrator(
        ApplicationDbContext context,
        ISensitiveDataProtector protector)
    {
        _context = context;
        _protector = protector;
    }

    public async Task<int> ProtectLegacyNotesAsync(CancellationToken cancellationToken = default)
    {
        var protectedCount = 0;
        var lastEntryId = 0;

        while (true)
        {
            var entries = await _context.MoodJournalEntries
                .Where(entry => entry.Id > lastEntryId && entry.Note != null)
                .OrderBy(entry => entry.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (entries.Count == 0)
            {
                break;
            }

            lastEntryId = entries[^1].Id;
            var changedInBatch = 0;
            foreach (var entry in entries)
            {
                if (entry.Note is null)
                {
                    continue;
                }

                if (entry.NoteProtectionVersion == 1)
                {
                    if (!_protector.IsProtected(entry.Note))
                    {
                        throw new InvalidOperationException(
                            "A journal note is marked protected but has an invalid protected-data envelope.");
                    }
                    // Validate the key ring while this migration scans rows. A missing or
                    // rotated-away key must fail startup rather than serving unusable data.
                    _protector.Unprotect(entry.Note);
                    continue;
                }

                if (entry.NoteProtectionVersion != 0)
                    throw new InvalidOperationException("A journal note has an unsupported protection version.");

                // A legacy row that happens to use the protection prefix is ambiguous. Try to
                // decrypt it before deciding; a failure is fail-closed rather than silently
                // double-encrypting data from a lost key ring.
                if (_protector.IsProtected(entry.Note))
                {
                    _protector.Unprotect(entry.Note);
                    entry.NoteProtectionVersion = 1;
                    changedInBatch++;
                    continue;
                }

                entry.Note = _protector.Protect(entry.Note);
                entry.NoteProtectionVersion = 1;
                changedInBatch++;
            }

            if (changedInBatch > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                protectedCount += changedInBatch;
            }

            // Avoid retaining an unbounded set of sensitive rows in memory during a
            // long-running legacy migration. Each batch is independently durable.
            _context.ChangeTracker.Clear();

            if (entries.Count < BatchSize)
            {
                break;
            }
        }

        return protectedCount;
    }

    /// <summary>Counts any note that would be exposed as legacy plaintext if backfill is skipped.</summary>
    public Task<int> CountLegacyNotesAsync(CancellationToken cancellationToken = default)
        => _context.MoodJournalEntries.CountAsync(
            entry => entry.Note != null && entry.NoteProtectionVersion != 1,
            cancellationToken);
}
