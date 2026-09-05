using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public sealed class RiskSnapshotAdministrationService : IRiskSnapshotAdministrationService
{
    private const int MaximumRows = 50_000;
    private const int MaximumErrorCount = 200;
    private const int MaximumFileBytes = 25 * 1024 * 1024;

    private static readonly string[] BaseHeaders =
    {
        "StudentNumber",
        "CourseKey",
        "WindowEndUtc",
        "ObservedDays",
        "FeatureSchemaVersion"
    };

    private readonly IRiskFeatureSnapshotRepository _snapshots;
    private readonly IRiskScoringRepository _scoringRepository;
    private readonly IRiskScoringService _scoringService;
    private readonly IRiskModelPredictor _predictor;
    private readonly TimeProvider _timeProvider;

    public RiskSnapshotAdministrationService(
        IRiskFeatureSnapshotRepository snapshots,
        IRiskScoringRepository scoringRepository,
        IRiskScoringService scoringService,
        IRiskModelPredictor predictor,
        TimeProvider timeProvider)
    {
        _snapshots = snapshots;
        _scoringRepository = scoringRepository;
        _scoringService = scoringService;
        _predictor = predictor;
        _timeProvider = timeProvider;
    }

    public async Task<RiskSnapshotStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var latestRun = await _scoringRepository.GetLatestRunAsync(cancellationToken);
        RiskModelDescriptor? descriptor = null;
        string? modelUnavailableReason = null;
        try
        {
            descriptor = _predictor.Descriptor;
        }
        catch (Exception exception)
        {
            modelUnavailableReason = string.IsNullOrWhiteSpace(exception.Message)
                ? "The risk model is unavailable."
                : exception.Message;
        }

        return await _snapshots.GetStatusAsync(
            descriptor,
            modelUnavailableReason,
            _timeProvider.GetUtcNow().UtcDateTime,
            RiskScoringPolicy.MaximumSnapshotAgeDays,
            latestRun is null ? null : RiskScoringService.ToRunDto(latestRun),
            cancellationToken);
    }

    public async Task<RiskSnapshotImportResultDto> ImportCsvAsync(
        Stream csv,
        string fileName,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (!csv.CanRead) throw new ArgumentException("The CSV stream must be readable.", nameof(csv));
        var safeFileName = RequiredCsvFileName(fileName);
        var actor = RequiredActor(actorUserId);

        if (csv.CanSeek && csv.Length > MaximumFileBytes)
            throw new InvalidDataException($"Snapshot CSV files are limited to {MaximumFileBytes / 1024 / 1024} MB.");
        using var buffered = new MemoryStream();
        await csv.CopyToAsync(buffered, cancellationToken);
        if (buffered.Length > MaximumFileBytes)
            throw new InvalidDataException($"Snapshot CSV files are limited to {MaximumFileBytes / 1024 / 1024} MB.");
        var fileSha256 = Convert.ToHexString(SHA256.HashData(buffered.ToArray())).ToLowerInvariant();
        buffered.Position = 0;

        using var reader = new StreamReader(
            buffered,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16_384,
            leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
            throw new InvalidDataException("The snapshot CSV is empty.");

        SnapshotImportLayout layout;
        try
        {
            layout = BuildHeaderIndexes(ParseCsvLine(headerLine));
        }
        catch (InvalidDataException exception)
        {
            return new RiskSnapshotImportResultDto(
                safeFileName,
                0,
                0,
                0,
                1,
                new[] { new RiskSnapshotImportErrorDto(1, exception.Message) });
        }
        var rows = new List<RiskFeatureSnapshotImportDto>();
        var errors = new List<RiskSnapshotImportErrorDto>();
        var rowNumber = 1;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (rowNumber > MaximumRows + 1)
                throw new InvalidDataException($"Snapshot CSV files are limited to {MaximumRows:N0} data rows.");

            try
            {
                var fields = ParseCsvLine(line);
                rows.Add(ParseRow(fields, layout, rowNumber));
            }
            catch (Exception exception) when (
                exception is FormatException or InvalidDataException or ArgumentOutOfRangeException)
            {
                errors.Add(new RiskSnapshotImportErrorDto(rowNumber, exception.Message));
            }
        }

        if (errors.Count > 0)
        {
            return new RiskSnapshotImportResultDto(
                safeFileName,
                rows.Count + errors.Count,
                0,
                0,
                errors.Count,
                errors.Take(MaximumErrorCount).ToArray());
        }

        return await _snapshots.ImportAsync(
            safeFileName,
            fileSha256,
            rows,
            errors,
            actor,
            cancellationToken);
    }

    public Task<RiskScoringRunDto> RunNowAsync(
        string actorUserId,
        CancellationToken cancellationToken = default)
        => _scoringService.RunPendingSnapshotsAsync(RequiredActor(actorUserId), cancellationToken);

    private static SnapshotImportLayout BuildHeaderIndexes(IReadOnlyList<string> headers)
    {
        var normalizedHeaders = headers
            .Select((header, index) => new { Header = header.Trim().TrimStart('\uFEFF'), Index = index })
            .ToArray();
        var duplicates = normalizedHeaders
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().Header)
            .OrderBy(header => header, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidDataException(
                $"The snapshot CSV contains duplicate column names: {string.Join(", ", duplicates)}.");
        }

        var indexes = normalizedHeaders
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);

        var missing = BaseHeaders.Where(header => !indexes.ContainsKey(header)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"The snapshot CSV is missing required columns: {string.Join(", ", missing)}.");

        var matches = new[]
            {
                RiskFeatureSchemas.Withdrawal28DayV1,
                RiskFeatureSchemas.Withdrawal28DayV2
            }
            .Where(schema => schema.FeatureNames.All(indexes.ContainsKey))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                "The snapshot CSV must contain exactly one registered feature-schema column set.");
        }

        return new SnapshotImportLayout(matches[0], indexes);
    }

    private static RiskFeatureSnapshotImportDto ParseRow(
        IReadOnlyList<string> fields,
        SnapshotImportLayout layout,
        int rowNumber)
    {
        string Value(string name)
        {
            var index = layout.Indexes[name];
            if (index >= fields.Count)
                throw new InvalidDataException($"Column {name} is missing a value.");
            return fields[index].Trim();
        }

        var studentNumber = Value("StudentNumber");
        if (string.IsNullOrWhiteSpace(studentNumber))
            throw new FormatException("StudentNumber is required.");
        var courseKey = Value("CourseKey");
        if (string.IsNullOrWhiteSpace(courseKey))
            throw new FormatException("CourseKey is required.");
        if (!DateTimeOffset.TryParse(
                Value("WindowEndUtc"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var windowEnd))
            throw new FormatException("WindowEndUtc must be a valid UTC timestamp.");
        if (!int.TryParse(Value("ObservedDays"), NumberStyles.None, CultureInfo.InvariantCulture, out var observedDays))
            throw new FormatException("ObservedDays must be an integer.");

        float FloatValue(string name)
        {
            if (!float.TryParse(Value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !float.IsFinite(value))
                throw new FormatException($"{name} must be a finite number.");
            return value;
        }

        var rowSchemaVersion = Value("FeatureSchemaVersion");
        if (!string.Equals(rowSchemaVersion, layout.Schema.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"FeatureSchemaVersion must be '{layout.Schema.Version}' for this CSV header.");
        }

        var values = layout.Schema.FeatureNames.Select(FloatValue).ToArray();
        if (string.Equals(layout.Schema.Version, RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal))
        {
            return new RiskFeatureSnapshotImportDto(
                studentNumber,
                courseKey,
                windowEnd.UtcDateTime,
                observedDays,
                rowSchemaVersion,
                values[0], values[1], values[2], values[3], values[4], values[5],
                rowNumber);
        }

        return new RiskFeatureSnapshotImportDto(
            studentNumber,
            courseKey,
            windowEnd.UtcDateTime,
            observedDays,
            rowSchemaVersion,
            0, 0, 0, 0, 0, 0,
            rowNumber,
            values[0], values[1], values[2], values[3], values[4], values[5],
            values[6], values[7], values[8], values[9], values[10], values[11]);
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (quoted) throw new InvalidDataException("A quoted CSV field was not terminated.");
        fields.Add(current.ToString());
        return fields;
    }

    private static string RequiredCsvFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A CSV file name is required.", nameof(fileName));
        var safeName = Path.GetFileName(fileName.Trim());
        if (!string.Equals(Path.GetExtension(safeName), ".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Risk snapshots must be supplied as a .csv file.", nameof(fileName));
        return safeName;
    }

    private static string RequiredActor(string actorUserId)
        => string.IsNullOrWhiteSpace(actorUserId)
            ? throw new ArgumentException("An actor user identifier is required.", nameof(actorUserId))
            : actorUserId.Trim();

    private sealed record SnapshotImportLayout(
        RiskFeatureSchemaDefinition Schema,
        IReadOnlyDictionary<string, int> Indexes);
}
