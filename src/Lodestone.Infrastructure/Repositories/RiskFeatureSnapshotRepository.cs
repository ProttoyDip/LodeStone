using System.Data;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

public sealed class RiskFeatureSnapshotRepository : IRiskFeatureSnapshotRepository
{
    private const int MaximumCourseKeyLength = 120;
    private const int MaximumReportedErrors = 200;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RiskFeatureSnapshotRepository(ApplicationDbContext context, TimeProvider timeProvider)
        => (_context, _timeProvider) = (context, timeProvider);

    public Task<RiskFeatureSnapshot?> GetByIdForScoringAsync(
        int snapshotId,
        DateTime asOfUtc,
        int maximumAgeDays,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = asOfUtc.AddDays(-maximumAgeDays);
        return _context.RiskFeatureSnapshots
            .AsNoTracking()
            .Include(snapshot => snapshot.StudentProfile)
                .ThenInclude(profile => profile!.User)
            .SingleOrDefaultAsync(
                snapshot => snapshot.Id == snapshotId &&
                            snapshot.WindowEndUtc >= cutoffUtc &&
                            snapshot.WindowEndUtc <= asOfUtc &&
                            snapshot.StudentProfile != null &&
                            snapshot.StudentProfile.User != null &&
                            snapshot.StudentProfile.User.IsActive &&
                            snapshot.StudentProfile.RiskMonitoringConsent != null &&
                            snapshot.StudentProfile.RiskMonitoringConsent.IsConsented,
                cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetPendingIdsAsync(
        RiskModelDescriptor descriptor,
        DateTime asOfUtc,
        int maximumAgeDays,
        int? studentProfileId = null,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = asOfUtc.AddDays(-maximumAgeDays);
        var snapshots = _context.RiskFeatureSnapshots
            .AsNoTracking()
            .Where(snapshot =>
                snapshot.FeatureSchemaVersion == descriptor.FeatureSchemaVersion &&
                snapshot.ObservedDays == descriptor.ObservedDays &&
                snapshot.WindowEndUtc >= cutoffUtc &&
                snapshot.WindowEndUtc <= asOfUtc &&
                snapshot.StudentProfile != null &&
                snapshot.StudentProfile.User != null &&
                snapshot.StudentProfile.User.IsActive &&
                snapshot.StudentProfile.RiskMonitoringConsent != null &&
                snapshot.StudentProfile.RiskMonitoringConsent.IsConsented &&
                !snapshot.RiskScores.Any(score => score.ModelVersion == descriptor.ModelVersion));

        if (studentProfileId.HasValue)
            snapshots = snapshots.Where(snapshot => snapshot.StudentProfileId == studentProfileId.Value);

        return await snapshots
            .OrderByDescending(snapshot => snapshot.WindowEndUtc)
            .ThenBy(snapshot => snapshot.StudentProfileId)
            .ThenBy(snapshot => snapshot.CourseKey)
            .Select(snapshot => snapshot.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<RiskSnapshotImportResultDto> ImportAsync(
        string fileName,
        string fileSha256,
        IReadOnlyList<RiskFeatureSnapshotImportDto> rows,
        IReadOnlyList<RiskSnapshotImportErrorDto> parseErrors,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260)
            throw new ArgumentException("A valid source file name is required.", nameof(fileName));
        if (fileSha256.Length != 64 || fileSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A lowercase or uppercase SHA-256 hex digest is required.", nameof(fileSha256));
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ArgumentException("An importing actor is required.", nameof(actorUserId));

        var errors = parseErrors.ToList();
        if (parseErrors.Count > 0)
            return Failure(fileName, rows.Count + RejectedRowCount(parseErrors), errors);

        foreach (var row in rows)
            ValidateRow(row, _timeProvider.GetUtcNow().UtcDateTime, errors);
        if (errors.Count > 0)
            return Failure(fileName, rows.Count, errors);

        var exactRows = new Dictionary<ExternalSnapshotKey, RiskFeatureSnapshotImportDto>();
        var duplicateRows = 0;
        foreach (var row in rows)
        {
            var normalized = Normalize(row);
            var key = ExternalSnapshotKey.From(normalized);
            if (!exactRows.TryGetValue(key, out var prior))
            {
                exactRows.Add(key, normalized);
                continue;
            }

            if (SameFeatures(prior, normalized))
            {
                duplicateRows++;
                continue;
            }

            AddError(errors, normalized.SourceRowNumber, "Conflicting values were supplied for the same student, course, window, and schema.");
        }
        if (errors.Count > 0)
            return Failure(fileName, rows.Count, errors);

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {

        var studentNumbers = exactRows.Values
            .Select(row => row.StudentNumber.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var profiles = await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.StudentNumber != null &&
                              studentNumbers.Contains(profile.StudentNumber.ToUpper()))
            .Select(profile => new
            {
                profile.Id,
                profile.StudentNumber,
                IsActive = profile.User != null && profile.User.IsActive,
                IsConsented = profile.RiskMonitoringConsent != null && profile.RiskMonitoringConsent.IsConsented
            })
            .ToListAsync(cancellationToken);
        var profileByNumber = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.StudentNumber))
            .ToDictionary(profile => profile.StudentNumber!.Trim(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<ResolvedImportRow>();
        foreach (var row in exactRows.Values)
        {
            if (!profileByNumber.TryGetValue(row.StudentNumber, out var profile))
            {
                AddError(errors, row.SourceRowNumber, $"StudentNumber '{row.StudentNumber}' was not found.");
                continue;
            }
            if (!profile.IsActive)
            {
                AddError(errors, row.SourceRowNumber, $"StudentNumber '{row.StudentNumber}' is inactive.");
                continue;
            }
            if (!profile.IsConsented)
            {
                AddError(errors, row.SourceRowNumber, $"StudentNumber '{row.StudentNumber}' has not consented to monitoring.");
                continue;
            }

            resolved.Add(new ResolvedImportRow(profile.Id, row));
        }

        var studentIds = resolved.Select(row => row.StudentProfileId).Distinct().ToArray();
        var minWindow = resolved.Count == 0 ? DateTime.MinValue : resolved.Min(row => row.Row.WindowEndUtc);
        var maxWindow = resolved.Count == 0 ? DateTime.MinValue : resolved.Max(row => row.Row.WindowEndUtc);
        var existing = resolved.Count == 0
            ? new List<RiskFeatureSnapshot>()
            : await _context.RiskFeatureSnapshots
                .AsNoTracking()
                .Where(snapshot => studentIds.Contains(snapshot.StudentProfileId) &&
                                   snapshot.WindowEndUtc >= minWindow &&
                                   snapshot.WindowEndUtc <= maxWindow)
                .ToListAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(
            InternalSnapshotKey.From);

        var toInsert = new List<RiskFeatureSnapshot>();
        foreach (var resolvedRow in resolved)
        {
            var row = resolvedRow.Row;
            var key = InternalSnapshotKey.From(resolvedRow.StudentProfileId, row);
            if (existingByKey.TryGetValue(key, out var prior))
            {
                if (SameFeatures(prior, row))
                {
                    duplicateRows++;
                    continue;
                }

                AddError(errors, row.SourceRowNumber, "Stored snapshot data conflicts with this row's values.");
                continue;
            }

            toInsert.Add(new RiskFeatureSnapshot
            {
                StudentProfileId = resolvedRow.StudentProfileId,
                CourseKey = row.CourseKey,
                WindowEndUtc = row.WindowEndUtc,
                ObservedDays = row.ObservedDays,
                FeatureSchemaVersion = row.FeatureSchemaVersion,
                SourceFileName = fileName,
                SourceFileSha256 = fileSha256,
                ActiveDayRate = row.ActiveDayRate,
                ActivitySpanDays = row.ActivitySpanDays,
                DaysSinceLastAccess = row.DaysSinceLastAccess,
                ForumInteractionCount = row.ForumInteractionCount,
                CourseInteractionCount = row.CourseInteractionCount,
                LateOrMissingAssignmentCount = row.LateOrMissingAssignmentCount,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                CreatedBy = actorUserId.Trim()
            });
        }

        // A conflicting duplicate invalidates the whole file. Unknown/inactive/unconsented
        // students are privacy skips and do not prevent other valid rows from importing.
        if (errors.Any(error => error.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase)))
            return Failure(fileName, rows.Count, errors);

        if (toInsert.Count > 0)
        {
            var candidateIds = toInsert.Select(snapshot => snapshot.StudentProfileId).Distinct().ToArray();
            var stillEligibleIds = await _context.StudentProfiles
                .AsNoTracking()
                .Where(profile => candidateIds.Contains(profile.Id) &&
                                  profile.User != null && profile.User.IsActive &&
                                  profile.RiskMonitoringConsent != null &&
                                  profile.RiskMonitoringConsent.IsConsented)
                .Select(profile => profile.Id)
                .ToListAsync(cancellationToken);
            var eligibleSet = stillEligibleIds.ToHashSet();
            var noLongerEligible = resolved
                .Where(item => candidateIds.Contains(item.StudentProfileId) &&
                               !eligibleSet.Contains(item.StudentProfileId))
                .Select(item => item.Row)
                .ToArray();
            foreach (var row in noLongerEligible)
                AddError(errors, row.SourceRowNumber, $"StudentNumber '{row.StudentNumber}' no longer has active monitoring consent.");
            toInsert.RemoveAll(snapshot => !eligibleSet.Contains(snapshot.StudentProfileId));
        }

        if (toInsert.Count > 0)
        {
            await _context.RiskFeatureSnapshots.AddRangeAsync(toInsert, cancellationToken);
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId.Trim(),
                Action = "RiskSnapshot.Import",
                EntityName = nameof(RiskFeatureSnapshot),
                Details = $"Imported {toInsert.Count} snapshot row(s) from {fileName}; SHA-256 {fileSha256}.",
                TimestampUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new RiskSnapshotImportResultDto(
            fileName,
            rows.Count,
            toInsert.Count,
            duplicateRows,
            RejectedRowCount(errors),
            errors.Take(MaximumReportedErrors).ToArray());
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<RiskSnapshotStatusDto> GetStatusAsync(
        RiskModelDescriptor? descriptor,
        string? modelUnavailableReason,
        DateTime asOfUtc,
        int maximumAgeDays,
        RiskScoringRunDto? latestRun,
        CancellationToken cancellationToken = default)
    {
        var snapshotCount = await _context.RiskFeatureSnapshots.AsNoTracking().CountAsync(cancellationToken);
        var consentedStudentCount = await _context.RiskMonitoringConsents
            .AsNoTracking()
            .CountAsync(consent => consent.IsConsented, cancellationToken);
        var latestWindow = await _context.RiskFeatureSnapshots
            .AsNoTracking()
            .OrderByDescending(snapshot => snapshot.WindowEndUtc)
            .Select(snapshot => (DateTime?)snapshot.WindowEndUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var pendingCount = descriptor is null
            ? 0
            : (await GetPendingIdsAsync(
                descriptor,
                asOfUtc,
                maximumAgeDays,
                null,
                cancellationToken)).Count;

        return new RiskSnapshotStatusDto(
            snapshotCount,
            consentedStudentCount,
            pendingCount,
            latestWindow,
            descriptor,
            modelUnavailableReason,
            latestRun);
    }

    private static void ValidateRow(
        RiskFeatureSnapshotImportDto row,
        DateTime asOfUtc,
        ICollection<RiskSnapshotImportErrorDto> errors)
    {
        if (string.IsNullOrWhiteSpace(row.StudentNumber))
            AddError(errors, row.SourceRowNumber, "StudentNumber is required.");
        if (string.IsNullOrWhiteSpace(row.CourseKey) || row.CourseKey.Trim().Length > MaximumCourseKeyLength)
            AddError(errors, row.SourceRowNumber, $"CourseKey must contain 1-{MaximumCourseKeyLength} characters.");
        if (row.WindowEndUtc == default)
            AddError(errors, row.SourceRowNumber, "WindowEndUtc is required.");
        else if (ToUtc(row.WindowEndUtc) > asOfUtc)
            AddError(errors, row.SourceRowNumber, "WindowEndUtc cannot be in the future.");
        if (row.ObservedDays != RiskFeatureSchema.Withdrawal28DayObservedDays)
            AddError(errors, row.SourceRowNumber, "ObservedDays must be 28.");
        if (!string.Equals(row.FeatureSchemaVersion?.Trim(), RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal))
            AddError(errors, row.SourceRowNumber, $"FeatureSchemaVersion must be '{RiskFeatureSchema.Withdrawal28DayV1}'.");

        var values = new[]
        {
            row.ActiveDayRate,
            row.ActivitySpanDays,
            row.DaysSinceLastAccess,
            row.ForumInteractionCount,
            row.CourseInteractionCount,
            row.LateOrMissingAssignmentCount
        };
        if (values.Any(value => !float.IsFinite(value) || value < 0))
            AddError(errors, row.SourceRowNumber, "Feature values must be finite and non-negative.");
        if (row.ActiveDayRate > 1)
            AddError(errors, row.SourceRowNumber, "ActiveDayRate must be between zero and one.");
        if (row.ActivitySpanDays > row.ObservedDays || row.DaysSinceLastAccess > row.ObservedDays)
            AddError(errors, row.SourceRowNumber, "Day-based features cannot exceed ObservedDays.");
    }

    private static RiskFeatureSnapshotImportDto Normalize(RiskFeatureSnapshotImportDto row)
        => row with
        {
            StudentNumber = row.StudentNumber.Trim(),
            CourseKey = row.CourseKey.Trim(),
            WindowEndUtc = ToUtc(row.WindowEndUtc),
            FeatureSchemaVersion = row.FeatureSchemaVersion.Trim()
        };

    private static bool SameFeatures(
        RiskFeatureSnapshotImportDto left,
        RiskFeatureSnapshotImportDto right)
        => left.ObservedDays == right.ObservedDays &&
           left.ActiveDayRate.Equals(right.ActiveDayRate) &&
           left.ActivitySpanDays.Equals(right.ActivitySpanDays) &&
           left.DaysSinceLastAccess.Equals(right.DaysSinceLastAccess) &&
           left.ForumInteractionCount.Equals(right.ForumInteractionCount) &&
           left.CourseInteractionCount.Equals(right.CourseInteractionCount) &&
           left.LateOrMissingAssignmentCount.Equals(right.LateOrMissingAssignmentCount);

    private static bool SameFeatures(RiskFeatureSnapshot left, RiskFeatureSnapshotImportDto right)
        => left.ObservedDays == right.ObservedDays &&
           left.ActiveDayRate.Equals(right.ActiveDayRate) &&
           left.ActivitySpanDays.Equals(right.ActivitySpanDays) &&
           left.DaysSinceLastAccess.Equals(right.DaysSinceLastAccess) &&
           left.ForumInteractionCount.Equals(right.ForumInteractionCount) &&
           left.CourseInteractionCount.Equals(right.CourseInteractionCount) &&
           left.LateOrMissingAssignmentCount.Equals(right.LateOrMissingAssignmentCount);

    private static RiskSnapshotImportResultDto Failure(
        string fileName,
        int totalRows,
        IReadOnlyCollection<RiskSnapshotImportErrorDto> errors)
        => new(
            fileName,
            totalRows,
            0,
            0,
            totalRows,
            errors.Take(MaximumReportedErrors).ToArray());

    private static void AddError(
        ICollection<RiskSnapshotImportErrorDto> errors,
        int rowNumber,
        string message)
        => errors.Add(new RiskSnapshotImportErrorDto(rowNumber, message));

    private static int RejectedRowCount(IEnumerable<RiskSnapshotImportErrorDto> errors)
        => errors.Select(error => error.RowNumber).Distinct().Count();

    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private sealed record ExternalSnapshotKey(
        string StudentNumber,
        string CourseKey,
        DateTime WindowEndUtc,
        string SchemaVersion)
    {
        public static ExternalSnapshotKey From(RiskFeatureSnapshotImportDto row)
            => new(
                row.StudentNumber.ToUpperInvariant(),
                row.CourseKey.ToUpperInvariant(),
                row.WindowEndUtc,
                row.FeatureSchemaVersion.ToUpperInvariant());
    }

    private sealed record InternalSnapshotKey(
        int StudentProfileId,
        string CourseKey,
        DateTime WindowEndUtc,
        string SchemaVersion)
    {
        public static InternalSnapshotKey From(RiskFeatureSnapshot snapshot)
            => From(
                snapshot.StudentProfileId,
                snapshot.CourseKey,
                snapshot.WindowEndUtc,
                snapshot.FeatureSchemaVersion);

        public static InternalSnapshotKey From(int studentProfileId, RiskFeatureSnapshotImportDto row)
            => From(studentProfileId, row.CourseKey, row.WindowEndUtc, row.FeatureSchemaVersion);

        private static InternalSnapshotKey From(
            int studentProfileId,
            string courseKey,
            DateTime windowEndUtc,
            string schemaVersion)
            => new(
                studentProfileId,
                courseKey.ToUpperInvariant(),
                windowEndUtc,
                schemaVersion.ToUpperInvariant());
    }

    private sealed record ResolvedImportRow(int StudentProfileId, RiskFeatureSnapshotImportDto Row);
}
