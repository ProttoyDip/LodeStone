using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lodestone.ML.Training;
using Microsoft.ML;

namespace Lodestone.ModelTrainer;

internal static class Program
{
    private const string DefaultDatasetUrl =
        "https://archive.ics.uci.edu/static/public/349/open%2Buniversity%2Blearning%2Banalytics%2Bdataset.zip";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            return command switch
            {
                "download" => await DownloadAsync(options),
                "train" => Train(options),
                _ => throw new CliUsageException($"Unknown command '{args[0]}'.")
            };
        }
        catch (CliUsageException exception)
        {
            Console.Error.WriteLine($"Usage error: {exception.Message}");
            Console.Error.WriteLine("Run with --help for command examples.");
            return 2;
        }
        catch (ModelQualityGateException exception)
        {
            Console.Error.WriteLine($"Model rejected: {exception.Message}");
            if (exception.FailureReportPath is not null)
                Console.Error.WriteLine($"Failure report: {exception.FailureReportPath}");
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> DownloadAsync(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "output", "url", "sha256");
        var outputPath = Path.GetFullPath(Get(options, "output", Path.Combine("src", "Lodestone.ML", "Data", "OULAD")));
        var sourceUrl = Get(options, "url", DefaultDatasetUrl);
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            || sourceUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CliUsageException("--url must be an absolute HTTPS URL.");
        }

        var expectedHash = options.GetValueOrDefault("sha256")?.Trim().ToLowerInvariant();
        ValidateOptionalHash(expectedHash, "--sha256");
        if (Directory.Exists(outputPath) || File.Exists(outputPath))
            throw new IOException($"Download output already exists: {outputPath}");

        var parent = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Download output has no parent directory.");
        Directory.CreateDirectory(parent);
        var stagingPath = Path.Combine(parent, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.staging");
        var archivePath = Path.Combine(Path.GetTempPath(), $"lodestone-oulad-{Guid.NewGuid():N}.zip");
        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var destination = new FileStream(
                    archivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    useAsync: true);
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            var sourceHash = ComputeSha256(archivePath);
            if (expectedHash is not null
                && !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash),
                    Convert.FromHexString(sourceHash)))
            {
                throw new InvalidDataException(
                    $"Downloaded archive SHA-256 {sourceHash} does not match the expected hash.");
            }

            ExtractSafely(archivePath, stagingPath);
            ValidateCanonicalTables(stagingPath);
            var provenance = new DatasetProvenance(
                sourceUri.AbsoluteUri,
                sourceHash,
                DateTime.UtcNow);
            await File.WriteAllTextAsync(
                Path.Combine(stagingPath, "source.json"),
                JsonSerializer.Serialize(provenance, JsonOptions));
            Directory.Move(stagingPath, outputPath);

            Console.WriteLine($"OULAD extracted to: {outputPath}");
            Console.WriteLine($"Source SHA-256: {sourceHash}");
            return 0;
        }
        finally
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }
    }

    private static int Train(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(
            options,
            "data", "model", "metadata", "report", "version", "source-url", "source-sha256",
            "seed", "min-auc", "min-recall", "min-precision");
        var dataPath = Path.GetFullPath(Get(options, "data", Path.Combine("src", "Lodestone.ML", "Data", "OULAD")));
        // The default publishes directly to the same content-root-relative location consumed by
        // src/Lodestone.Web/appsettings.json, so a successful train integrates on next restart.
        var modelPath = Path.GetFullPath(Get(
            options,
            "model",
            Path.Combine("src", "Lodestone.Web", "App_Data", "ml", "risk-model.zip")));
        var metadataPath = Path.GetFullPath(Get(
            options,
            "metadata",
            Path.ChangeExtension(modelPath, ".metadata.json")));
        var reportPath = Path.GetFullPath(Get(
            options,
            "report",
            Path.Combine("src", "Lodestone.ML", "Reports", "risk-model.report.json")));
        var provenance = ReadProvenance(dataPath);
        var sourceUrl = options.GetValueOrDefault("source-url") ?? provenance?.SourceUrl;
        var sourceHash = options.GetValueOrDefault("source-sha256") ?? provenance?.Sha256;
        ValidateOptionalHash(sourceHash, "--source-sha256");

        var mlContext = new MLContext(seed: ParseInt(options, "seed", 42));
        var loader = new OuladDataLoader(mlContext);
        var features = new FeatureEngineering(mlContext);
        var trainer = new global::Lodestone.ML.Training.ModelTrainer(mlContext);
        var evaluator = new ModelEvaluator(mlContext);
        var pipeline = new TrainingPipeline(mlContext, loader, features, trainer, evaluator);
        var result = pipeline.Run(new TrainingOptions
        {
            DataDirectory = dataPath,
            ModelOutputPath = modelPath,
            MetadataOutputPath = metadataPath,
            ReportOutputPath = reportPath,
            ModelVersion = options.GetValueOrDefault("version"),
            SourceUrl = sourceUrl,
            SourceSha256 = sourceHash,
            Seed = ParseInt(options, "seed", 42),
            MinimumTestAreaUnderRocCurve = ParseDouble(options, "min-auc", 0.70),
            MinimumRecall = ParseDouble(options, "min-recall", 0.70),
            MinimumPrecision = ParseDouble(options, "min-precision", 0.30)
        });

        Console.WriteLine($"Model accepted: {result.Metadata.ModelVersion}");
        Console.WriteLine($"Model: {result.ModelPath}");
        Console.WriteLine($"Metadata: {result.MetadataPath}");
        Console.WriteLine($"Report: {result.ReportPath}");
        Console.WriteLine(
            $"Test AUC={result.Report.TestMetrics.AreaUnderRocCurve:F3}, " +
            $"recall={result.Report.TestMetrics.Recall:F3}, " +
            $"precision={result.Report.TestMetrics.Precision:F3}, " +
            $"threshold={result.Metadata.DecisionThreshold:F4}");
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || args[index].Length == 2)
                throw new CliUsageException($"Expected an option beginning with --; received '{args[index]}'.");
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new CliUsageException($"Option '{args[index]}' requires a value.");

            var name = args[index][2..];
            if (!result.TryAdd(name, args[index + 1]))
                throw new CliUsageException($"Option '--{name}' was specified more than once.");
        }

        return result;
    }

    private static void EnsureOnly(IReadOnlyDictionary<string, string> values, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = values.Keys.FirstOrDefault(key => !allowedSet.Contains(key));
        if (unknown is not null)
            throw new CliUsageException($"Unknown option '--{unknown}'.");
    }

    private static string Get(IReadOnlyDictionary<string, string> options, string key, string fallback)
        => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int ParseInt(IReadOnlyDictionary<string, string> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out var value))
            return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new CliUsageException($"--{key} must be a non-negative integer.");
        return parsed;
    }

    private static double ParseDouble(IReadOnlyDictionary<string, string> options, string key, double fallback)
    {
        if (!options.TryGetValue(key, out var value))
            return fallback;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw new CliUsageException($"--{key} must be an invariant number.");
        return parsed;
    }

    private static DatasetProvenance? ReadProvenance(string dataPath)
    {
        var path = Path.Combine(dataPath, "source.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<DatasetProvenance>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The OULAD source.json file is empty.");
    }

    private static void ValidateOptionalHash(string? value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var normalized = value.Trim();
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new CliUsageException($"{option} must be a 64-character hexadecimal SHA-256 value.");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ExtractSafely(string archivePath, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!destination.StartsWith(root, pathComparison))
                throw new InvalidDataException("The downloaded ZIP contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }

    private static void ValidateCanonicalTables(string directory)
    {
        string[] required =
        [
            "courses.csv", "assessments.csv", "vle.csv", "studentAssessment.csv",
            "studentInfo.csv", "studentRegistration.csv", "studentVle.csv"
        ];
        var files = Directory.EnumerateFiles(directory, "*.csv", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var table in required)
        {
            if (!files.TryGetValue(table, out var count) || count != 1)
                throw new InvalidDataException($"Downloaded archive must contain exactly one '{table}'.");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Lodestone OULAD withdrawal-model trainer

            Download the official UCI dataset (dataset 349):
              dotnet run --project tools/Lodestone.ModelTrainer -- download [--output <directory>] [--url <https-url>] [--sha256 <expected-hash>]

            Train, validate and atomically publish an artifact:
              dotnet run --project tools/Lodestone.ModelTrainer -- train [--data <directory>] [--model <risk-model.zip>] [--metadata <json>] [--report <json>] [--version <id>] [--source-url <url>] [--source-sha256 <hash>] [--seed <number>] [--min-auc <0..1>] [--min-recall <0..1>] [--min-precision <0..1>]

            By default train publishes model and metadata to src/Lodestone.Web/App_Data/ml, the
            location consumed by the Web app after MachineLearning:Enabled is set true, and writes
            its evaluation report to src/Lodestone.ML/Reports.

            The train command exits with code 3 and leaves the prior artifact untouched when the
            validation threshold or untouched test quality gate fails.
            """);
    }

    private sealed record DatasetProvenance(string SourceUrl, string Sha256, DateTime DownloadedAtUtc);
    private sealed class CliUsageException(string message) : Exception(message);
}
