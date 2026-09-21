namespace Lodestone.Application.Interfaces;

/// <summary>
/// Builds a synthetic snapshot CSV for demonstrations. The values are invented and only meant to
/// exercise the import, scoring and queue flow; they must never stand in for real learning data.
/// </summary>
public interface IDemoSnapshotSampleService
{
    /// <summary>
    /// One row per currently verified, consented and active student, dated today under a fresh course
    /// key so the file imports as new snapshots every time. Returns null when the schema is unsupported.
    /// </summary>
    Task<string?> BuildCsvAsync(string featureSchemaVersion, DateTime nowUtc, CancellationToken cancellationToken = default);
}
