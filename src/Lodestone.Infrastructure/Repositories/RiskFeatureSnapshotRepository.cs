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
                            snapshot.StudentProfile.StudentNumber != null &&
                            snapshot.StudentProfile.StudentNumber != "" &&
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
                snapshot.StudentProfile.StudentNumber != null &&
                snapshot.StudentProfile.StudentNumber != "" &&
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

            toInsert.Add(CreateSnapshot(
                resolvedRow.StudentProfileId,
                row,
                fileName,
                fileSha256,
                _timeProvider.GetUtcNow().UtcDateTime,
                actorUserId.Trim()));
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
        if (!RiskFeatureSchemas.TryGet(row.FeatureSchemaVersion?.Trim(), out var schema))
        {
            AddError(errors, row.SourceRowNumber, "FeatureSchemaVersion is not supported by the running application.");
            return;
        }
        if (row.ObservedDays != schema.ObservedDays)
            AddError(errors, row.SourceRowNumber, $"ObservedDays must be {schema.ObservedDays} for '{schema.Version}'.");

        IReadOnlyList<float> values;
        try
        {
            values = row.GetFeatureValues();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            AddError(errors, row.SourceRowNumber, exception.Message);
            return;
        }

        if (values.Any(value => !float.IsFinite(value)))
        {
            AddError(errors, row.SourceRowNumber, "Feature values must be finite.");
            return;
        }

        if (string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal))
        {
            if (values.Any(value => value < 0))
                AddError(errors, row.SourceRowNumber, "Feature values must be non-negative.");
            if (row.ActiveDayRate > 1)
                AddError(errors, row.SourceRowNumber, "ActiveDayRate must be between zero and one.");
            if (row.ActivitySpanDays > row.ObservedDays || row.DaysSinceLastAccess > row.ObservedDays)
                AddError(errors, row.SourceRowNumber, "Day-based features cannot exceed ObservedDays.");
            return;
        }

        // V2 explicitly permits signed trend features at indices 2 and 5 only.
        if (values.Where((_, index) => index is not 2 and not 5).Any(value => value < 0) ||
            values.Where((_, index) => index is 2 or 5).Any(value => value is < -1 or > 1) ||
            row.RecentActiveDayRate > 1 || row.PriorActiveDayRate > 1 ||
            row.InactivityStreakDays > row.ObservedDays ||
            row.AssessmentOnTimeRate > 1 || row.AssessmentLateOrMissingRate > 1 ||
            row.CourseProgressRatio > 1 || row.CohortActivityPercentile > 1)
        {
            AddError(errors, row.SourceRowNumber, "V2 feature values are outside their valid range.");
        }
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
           left.FeatureSchemaVersion.Equals(right.FeatureSchemaVersion, StringComparison.Ordinal) &&
           left.GetFeatureValues().SequenceEqual(right.GetFeatureValues());

    private static bool SameFeatures(RiskFeatureSnapshot left, RiskFeatureSnapshotImportDto right)
        => left.ObservedDays == right.ObservedDays &&
           left.FeatureSchemaVersion.Equals(right.FeatureSchemaVersion, StringComparison.Ordinal) &&
           SnapshotFeatureValues(left).SequenceEqual(right.GetFeatureValues());

    private static RiskFeatureSnapshot CreateSnapshot(
        int studentProfileId,
        RiskFeatureSnapshotImportDto row,
        string fileName,
        string fileSha256,
        DateTime createdAtUtc,
        string createdBy)
    {
        var snapshot = new RiskFeatureSnapshot
        {
            StudentProfileId = studentProfileId,
            CourseKey = row.CourseKey,
            WindowEndUtc = row.WindowEndUtc,
            ObservedDays = row.ObservedDays,
            FeatureSchemaVersion = row.FeatureSchemaVersion,
            SourceFileName = fileName,
            SourceFileSha256 = fileSha256,
            // These v1 columns deliberately remain zero for v2. The schema/version is what
            // selects features, and nullable v2 columns ensure the two contracts cannot blend.
            ActiveDayRate = row.ActiveDayRate,
            ActivitySpanDays = row.ActivitySpanDays,
            DaysSinceLastAccess = row.DaysSinceLastAccess,
            ForumInteractionCount = row.ForumInteractionCount,
            CourseInteractionCount = row.CourseInteractionCount,
            LateOrMissingAssignmentCount = row.LateOrMissingAssignmentCount,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy
        };
        if (string.Equals(row.FeatureSchemaVersion, RiskFeatureSchema.Withdrawal28DayV2, StringComparison.Ordinal))
        {
            snapshot.RecentActiveDayRate = row.RecentActiveDayRate;
            snapshot.PriorActiveDayRate = row.PriorActiveDayRate;
            snapshot.ActiveDayRateTrend = row.ActiveDayRateTrend;
            snapshot.RecentCourseClickRate = row.RecentCourseClickRate;
            snapshot.PriorCourseClickRate = row.PriorCourseClickRate;
            snapshot.CourseClickRateTrend = row.CourseClickRateTrend;
            snapshot.InactivityStreakDays = row.InactivityStreakDays;
            snapshot.AssessmentDueRate = row.AssessmentDueRate;
            snapshot.AssessmentOnTimeRate = row.AssessmentOnTimeRate;
            snapshot.AssessmentLateOrMissingRate = row.AssessmentLateOrMissingRate;
            snapshot.CourseProgressRatio = row.CourseProgressRatio;
            snapshot.CohortActivityPercentile = row.CohortActivityPercentile;
        }

        return snapshot;
    }

    private static IReadOnlyList<float> SnapshotFeatureValues(RiskFeatureSnapshot snapshot)
        => snapshot.FeatureSchemaVersion switch
        {
            RiskFeatureSchema.Withdrawal28DayV1 =>
            [
                snapshot.ActiveDayRate,
                snapshot.ActivitySpanDays,
                snapshot.DaysSinceLastAccess,
                snapshot.ForumInteractionCount,
                snapshot.CourseInteractionCount,
                snapshot.LateOrMissingAssignmentCount
            ],
            RiskFeatureSchema.Withdrawal28DayV2 =>
            [
                Required(snapshot.RecentActiveDayRate, nameof(snapshot.RecentActiveDayRate)),
                Required(snapshot.PriorActiveDayRate, nameof(snapshot.PriorActiveDayRate)),
                Required(snapshot.ActiveDayRateTrend, nameof(snapshot.ActiveDayRateTrend)),
                Required(snapshot.RecentCourseClickRate, nameof(snapshot.RecentCourseClickRate)),
                Required(snapshot.PriorCourseClickRate, nameof(snapshot.PriorCourseClickRate)),
                Required(snapshot.CourseClickRateTrend, nameof(snapshot.CourseClickRateTrend)),
                Required(snapshot.InactivityStreakDays, nameof(snapshot.InactivityStreakDays)),
                Required(snapshot.AssessmentDueRate, nameof(snapshot.AssessmentDueRate)),
                Required(snapshot.AssessmentOnTimeRate, nameof(snapshot.AssessmentOnTimeRate)),
                Required(snapshot.AssessmentLateOrMissingRate, nameof(snapshot.AssessmentLateOrMissingRate)),
                Required(snapshot.CourseProgressRatio, nameof(snapshot.CourseProgressRatio)),
                Required(snapshot.CohortActivityPercentile, nameof(snapshot.CohortActivityPercentile))
            ],
            _ => throw new InvalidOperationException("The stored snapshot has an unsupported feature schema.")
        };

    private static float Required(float? value, string name)
        => value ?? throw new InvalidOperationException($"The stored snapshot is missing '{name}'.");

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
