using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Evaluation;
using Lodestone.ML.Models;
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
                "train" => Train(options, schemaVersion: RiskFeatureSchema.Withdrawal28DayV1),
                "experiment-v2" => Train(options, schemaVersion: RiskFeatureSchema.Withdrawal28DayV2),
                "experiment-v3" => Train(options, schemaVersion: RiskFeatureSchema.Withdrawal28DayV3),
                "analyze" => Analyze(options),
                "audit-fairness" => AuditFairness(options),
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

    private static int Train(IReadOnlyDictionary<string, string> options, string schemaVersion)
    {
        EnsureOnly(
            options,
            "data", "model", "metadata", "report", "version", "source-url", "source-sha256",
            "seed");
        var isExperiment = !string.Equals(schemaVersion, RiskFeatureSchema.Withdrawal28DayV1, StringComparison.Ordinal);
        var experimentSlug = schemaVersion switch
        {
            RiskFeatureSchema.Withdrawal28DayV2 => "v2",
            RiskFeatureSchema.Withdrawal28DayV3 => "v3",
            _ => "v1"
        };
        var experimentName = isExperiment ? $"experiment-{experimentSlug}" : "train-v1";
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
            isExperiment
                ? Path.Combine("src", "Lodestone.ML", "Reports", "experiments", $"risk-model.{experimentSlug}.report.json")
                : Path.Combine("src", "Lodestone.ML", "Reports", "risk-model.report.json")));
        var provenance = ReadProvenance(dataPath);
        var sourceUrl = options.GetValueOrDefault("source-url") ?? provenance?.SourceUrl;
        var sourceHash = options.GetValueOrDefault("source-sha256") ?? provenance?.Sha256;
        ValidateOptionalHash(sourceHash, "--source-sha256");

        var seed = ParseInt(options, "seed", isExperiment ? 20260831 : 42);
        var mlContext = new MLContext(seed: seed);
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
            Seed = seed,
            FeatureSchemaVersion = schemaVersion,
            UseV2Experiment = isExperiment,
            ExperimentName = experimentName
        });

        Console.WriteLine($"Model accepted: {result.Metadata.ModelVersion}");
        Console.WriteLine($"Model: {result.ModelPath}");
        Console.WriteLine($"Metadata: {result.MetadataPath}");
        Console.WriteLine($"Publication manifest: {result.PublicationManifestPath}");
        Console.WriteLine($"Report: {result.ReportPath}");
        Console.WriteLine(
            $"Test AUC={result.Report.TestMetrics!.AreaUnderRocCurve:F3}, " +
            $"recall={result.Report.TestMetrics.Recall:F3}, " +
            $"precision={result.Report.TestMetrics.Precision:F3}, " +
            $"threshold={result.Metadata.DecisionThreshold:F4}");
        return 0;
    }

    /// <summary>
    /// Diagnostic-only. Reports what precision is actually attainable at each recall floor so gate
    /// values can be chosen from measurements. Publishes nothing and never touches locked test.
    /// </summary>
    private static int Analyze(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "data", "schema", "report", "seed", "candidate", "weighting", "target");
        var dataPath = Path.GetFullPath(Get(options, "data", Path.Combine("src", "Lodestone.ML", "Data", "OULAD")));
        var schemaVersion = Get(options, "schema", RiskFeatureSchema.Withdrawal28DayV3);
        _ = RiskFeatureSchemas.GetRequired(schemaVersion);
        var seed = ParseInt(options, "seed", 20260831);
        var weightStrategy = ParseWeightStrategy(options.GetValueOrDefault("weighting"));
        var labelStrategy = ParseLabelStrategy(options.GetValueOrDefault("target"));
        var requestedCandidate = options.GetValueOrDefault("candidate");
        var reportIdentity = string.Join(
            '.',
            schemaVersion,
            labelStrategy.ToString().ToLowerInvariant(),
            weightStrategy.ToString().ToLowerInvariant(),
            requestedCandidate ?? "default-candidates");
        var reportPath = Path.GetFullPath(Get(
            options,
            "report",
            Path.Combine("src", "Lodestone.ML", "Reports", "experiments", $"threshold-analysis.{reportIdentity}.json")));

        var candidates = string.IsNullOrWhiteSpace(requestedCandidate)
            ? ModelTrainingCandidate.V2Candidates
                .Where(item => item.Id is "fasttree-200-31-10-0.05" or "lightgbm-300-31-20-0.05")
                .ToArray()
            : ModelTrainingCandidate.V2Candidates
                .Where(item => string.Equals(item.Id, requestedCandidate, StringComparison.Ordinal))
                .ToArray();
        if (candidates.Length == 0)
            throw new CliUsageException($"Unknown candidate '{requestedCandidate}'.");

        var mlContext = new MLContext(seed: seed);
        var analyzer = new ThresholdAnalyzer(
            mlContext,
            new OuladDataLoader(mlContext),
            new FeatureEngineering(mlContext),
            new global::Lodestone.ML.Training.ModelTrainer(mlContext),
            new ModelEvaluator(mlContext));
        var report = analyzer.Analyze(dataPath, schemaVersion, candidates, seed, weightStrategy, labelStrategy);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));

        Console.WriteLine($"Schema: {report.FeatureSchemaVersion}");
        Console.WriteLine($"Training weighting: {report.TrainingWeightStrategy}");
        Console.WriteLine($"Label strategy: {report.LabelStrategy}");
        Console.WriteLine($"Split stratification label: {report.SplitLabelStrategy}");
        Console.WriteLine(
            $"Validation rows: {report.ValidationRows:N0}, positives: {report.ValidationPositives:N0} " +
            $"({report.ValidationPositiveRate:P2} base rate)");
        foreach (var candidate in report.Candidates)
        {
            Console.WriteLine(
                $"\n{candidate.CandidateId} ({candidate.Algorithm}) — " +
                $"ROC AUC {candidate.AreaUnderRocCurve:F4}, PR AUC {candidate.AreaUnderPrecisionRecallCurve:F4}; " +
                "best precision at each recall floor:");
            foreach (var point in candidate.BestPrecisionAtOrAboveRecall)
            {
                Console.WriteLine(point.IsAttainable
                    ? $"  recall >= {point.RecallFloor:F2} -> precision {point.BestPrecision:F4} (at recall {point.RecallAtBestPrecision:F3}, threshold {point.ThresholdAtBestPrecision:F4})"
                    : $"  recall >= {point.RecallFloor:F2} -> unattainable");
            }
        }

        Console.WriteLine($"\nReport: {reportPath}");
        return 0;
    }

    /// <summary>
    /// Reports how the published model performs across student subgroups it was never trained on.
    /// Loads the existing artifact, scores a held-out partition, and joins each row to the OULAD
    /// demographics. It trains nothing, changes no artifact, and writes only its own report.
    /// </summary>
    private static int AuditFairness(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(
            options,
            "data", "model", "metadata", "report", "partition", "queue-threshold",
            "min-group-rows", "min-group-positives", "expect-student-hash");
        var dataPath = Path.GetFullPath(Get(options, "data", Path.Combine("src", "Lodestone.ML", "Data", "OULAD")));
        var modelPath = Path.GetFullPath(Get(
            options,
            "model",
            Path.Combine("src", "Lodestone.Web", "App_Data", "ml", "risk-model.zip")));
        var metadataPath = Path.GetFullPath(Get(
            options,
            "metadata",
            Path.ChangeExtension(modelPath, ".metadata.json")));
        var partition = Get(options, "partition", "test").Trim().ToLowerInvariant();
        var reportPath = Path.GetFullPath(Get(
            options,
            "report",
            Path.Combine("src", "Lodestone.ML", "Reports", $"fairness-audit.{partition}.json")));

        if (!File.Exists(modelPath))
            throw new CliUsageException($"No published model at '{modelPath}'. Train one first.");
        if (!File.Exists(metadataPath))
            throw new CliUsageException($"No model metadata at '{metadataPath}'.");

        var metadata = JsonSerializer.Deserialize<RiskModelMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("The model metadata file is empty.");

        var operatingPoints = new List<(string Name, double Threshold)>();
        if (options.TryGetValue("queue-threshold", out var queueValue))
        {
            if (!double.TryParse(queueValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var queueThreshold)
                || !double.IsFinite(queueThreshold) || queueThreshold is < 0 or > 1)
            {
                throw new CliUsageException("--queue-threshold must be a probability between 0 and 1.");
            }

            operatingPoints.Add(("queue-threshold", queueThreshold));
        }

        var mlContext = new MLContext(seed: metadata.Seed);
        var auditor = new FairnessAuditor(mlContext, new OuladDataLoader(mlContext));
        var report = auditor.Run(
            new FairnessAuditOptions
            {
                DataDirectory = dataPath,
                ModelPath = modelPath,
                MetadataPath = metadataPath,
                Partition = partition,
                AdditionalOperatingPoints = operatingPoints,
                MinimumGroupRows = ParseInt(options, "min-group-rows", 500),
                MinimumGroupPositives = ParseInt(options, "min-group-positives", 20),
                ExpectedPartitionStudentHash = options.GetValueOrDefault("expect-student-hash")
            },
            metadata);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
        PrintFairnessSummary(report);
        Console.WriteLine($"{Environment.NewLine}Report: {reportPath}");
        return 0;
    }

    private static void PrintFairnessSummary(FairnessAuditReport report)
    {
        Console.WriteLine($"Model: {report.ModelVersion} ({report.FeatureSchemaVersion})");
        Console.WriteLine(
            $"Partition: {report.Partition} - {report.RowCount:N0} student-weeks, " +
            $"{report.StudentCount:N0} students, base rate {report.BaseRate:P2}");
        Console.WriteLine(
            $"Partition student hash: {report.PartitionStudentHash}"
            + (report.PartitionHashVerified ? " (verified against the training report)" : " (unverified)"));
        if (report.UnmatchedRowCount > 0)
            Console.WriteLine($"Unmatched rows excluded: {report.UnmatchedRowCount:N0}");
        Console.WriteLine();
        Console.WriteLine(
            "The model never sees these attributes. Gaps below are disparate impact, not disparate");
        Console.WriteLine(
            "treatment: they show where a behaviour-only signal works less well for some students.");

        foreach (var point in report.OperatingPoints)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"=== {point.Name} (threshold {point.Threshold:F4}) - overall recall " +
                $"{point.Overall.Recall:P1}, precision {point.Overall.Precision:P1}, " +
                $"flagging {point.Overall.SelectionRate:P2} of student-weeks ===");

            foreach (var attribute in point.Attributes)
            {
                Console.WriteLine();
                Console.WriteLine($"  {attribute.Attribute}");
                foreach (var group in attribute.Groups)
                {
                    Console.WriteLine(
                        $"    {group.Group,-30} n={group.RowCount,7:N0} ({group.StudentCount,5:N0} students)  " +
                        $"base={group.BaseRate,7:P2}  recall={group.Recall,6:P1}  " +
                        $"precision={group.Precision,6:P1}  FPR={group.FalsePositiveRate,7:P2}  " +
                        $"AUC={FormatOptional(group.AreaUnderRocCurve)}");
                }

                foreach (var suppressed in attribute.SuppressedGroups)
                {
                    Console.WriteLine(
                        $"    {suppressed.Group,-30} n={suppressed.RowCount,7:N0} " +
                        $"({suppressed.StudentCount,5:N0} students)  suppressed: {suppressed.Reason}");
                }

                if (attribute.Groups.Count >= 2)
                {
                    Console.WriteLine(
                        $"    -> recall gap {attribute.RecallGap:P1}, precision gap {attribute.PrecisionGap:P1}, " +
                        $"FPR gap {attribute.FalsePositiveRateGap:P2}, " +
                        $"selection-rate ratio {attribute.SelectionRateRatio:F3} " +
                        $"(base-rate gap {attribute.BaseRateGap:P2})");
                }
            }
        }
    }

    private static string FormatOptional(double? value)
        => value is null ? "   n/a" : value.Value.ToString("F3", CultureInfo.InvariantCulture);

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

    private static TrainingWeightStrategy ParseWeightStrategy(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "balanced" => TrainingWeightStrategy.Balanced,
            "sqrt" or "square-root-balanced" => TrainingWeightStrategy.SquareRootBalanced,
            "none" or "unweighted" => TrainingWeightStrategy.Unweighted,
            _ => throw new CliUsageException(
                "--weighting must be balanced, square-root-balanced, or unweighted.")
        };

    private static WithdrawalLabelStrategy ParseLabelStrategy(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "28-day" or "within-28-days" => WithdrawalLabelStrategy.Within28Days,
            "eventual" or "eventual-withdrawal" => WithdrawalLabelStrategy.EventualWithdrawal,
            "non-completion" or "eventual-non-completion" => WithdrawalLabelStrategy.EventualNonCompletion,
            _ => throw new CliUsageException(
                "--target must be within-28-days, eventual-withdrawal, or eventual-non-completion.")
        };

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

            Train the legacy six-feature contract, validate it, and atomically publish only if all
            fixed acceptance gates pass:
              dotnet run --project tools/Lodestone.ModelTrainer -- train [--data <directory>] [--model <risk-model.zip>] [--metadata <json>] [--report <json>] [--version <id>] [--source-url <url>] [--source-sha256 <hash>] [--seed <number>]

            Run the next runtime-capable, twelve-feature v2 experiment. It uses a deterministic
            grouped 70/15/15 split, grouped CV within training, FastTree + LightGBM candidates,
            validation-only selection, then exactly one locked-test evaluation. A successful
            candidate is atomically published to the same application artifact location:
              dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v2 [--data <directory>] [--model <risk-model.zip>] [--metadata <json>] [--report <json>] [--version <id>] [--source-url <url>] [--source-sha256 <hash>] [--seed <number>]

            Same protocol as experiment-v2, using the seventeen-feature v3 schema (v2's twelve
            features plus activity acceleration, click volatility, forum-engagement share, weekly
            inactivity coverage, and an assessment-miss streak; still clickstream/assessment-only,
            no demographic or registration data):
              dotnet run --project tools/Lodestone.ModelTrainer -- experiment-v3 [--data <directory>] [--model <risk-model.zip>] [--metadata <json>] [--report <json>] [--version <id>] [--source-url <url>] [--source-sha256 <hash>] [--seed <number>]

            By default train publishes model and metadata to src/Lodestone.Web/App_Data/ml, the
            location consumed by the Web app after MachineLearning:Enabled is set true, and writes
            its evaluation report to src/Lodestone.ML/Reports (v2 reports use Reports/experiments).

            Report what precision is attainable at each recall floor, to choose gate values from
            measurement rather than assumption. Trains on the training split and scores validation
            only; it publishes nothing and never touches the locked test partition:
              dotnet run --project tools/Lodestone.ModelTrainer -- analyze [--data <directory>] [--schema <feature-schema>] [--report <json>] [--seed <number>] [--candidate <id>] [--weighting <balanced|square-root-balanced|unweighted>] [--target <within-28-days|eventual-withdrawal|eventual-non-completion>]

            Audit the published model for subgroup performance across gender, age band, deprivation
            band, disability, prior education and region. These attributes are never trained on and
            never stored by the application; the audit reads them from OULAD only, to measure
            disparate impact. It trains nothing and publishes nothing:
              dotnet run --project tools/Lodestone.ModelTrainer -- audit-fairness [--data <directory>] [--model <risk-model.zip>] [--metadata <json>] [--report <json>] [--partition <test|validation>] [--queue-threshold <probability>] [--min-group-rows <n>] [--min-group-positives <n>] [--expect-student-hash <sha256>]

            Pass --queue-threshold with the deployed MachineLearning:QueueThreshold value: fairness
            is a property of an operating point, and the threshold that decides whose name reaches a
            counselor matters more than the one stamped into the artifact. Pass --expect-student-hash
            with testStudentHash from the training report to prove the audit scored the same
            partition the published metrics came from.

            Every training command uses the fixed AUC >= .70, recall >= .70, precision >= .30 gate. The
            locked test partition is never evaluated if validation fails. Exit code 3 leaves any
            previously published application artifact untouched; failure reports stay outside the
            Web App_Data/ml directory.
            """);
    }

    private sealed record DatasetProvenance(string SourceUrl, string Sha256, DateTime DownloadedAtUtc);
    private sealed class CliUsageException(string message) : Exception(message);
}
