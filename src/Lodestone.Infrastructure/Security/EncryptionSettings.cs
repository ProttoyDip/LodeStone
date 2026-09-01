namespace Lodestone.Infrastructure.Security;

public class EncryptionSettings
{
    public const string SectionName = "Encryption";
    public string KeyRingPath { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "Lodestone";
    /// <summary>
    /// When true, startup refuses to serve a database that still contains legacy plaintext
    /// journal notes if the explicit backfill switch is off.
    /// </summary>
    public bool RequireProtectedJournalNotes { get; set; } = true;
}
