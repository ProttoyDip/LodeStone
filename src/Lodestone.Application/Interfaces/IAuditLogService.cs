namespace Lodestone.Application.Interfaces;

/// <summary>
/// Records an entry to the security/privacy audit trail. Stages the entry on the current
/// unit of work without saving — the caller's own SaveChangesAsync persists it alongside
/// the action it describes.
/// </summary>
public interface IAuditLogService
{
    void Record(string action, string? entityName = null, string? entityId = null, string? details = null);
}
