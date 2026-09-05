using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

/// <summary>Optional private mood journal entry owned by a student.</summary>
public class MoodJournalEntry : SoftDeleteEntity
{
    public int StudentProfileId { get; set; }
    public StudentProfile? StudentProfile { get; set; }

    public int MoodRating { get; set; }        // e.g. 1..5
    public string? Note { get; set; }
    /// <summary>
    /// Storage-only protection marker: 0 is a legacy plaintext row awaiting backfill and 1 is a
    /// Data-Protection encrypted value. It prevents a prefix-looking plaintext note from being
    /// silently mistaken for ciphertext.
    /// </summary>
    public int NoteProtectionVersion { get; set; }
    public DateTime EntryDateUtc { get; set; }
}
