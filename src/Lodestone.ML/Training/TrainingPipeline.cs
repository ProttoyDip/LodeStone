using System.Text.Json;
using Lodestone.ML.Models;
using Lodestone.ML.Prediction;
using Microsoft.ML;

namespace Lodestone.ML.Training;

/// <summary>End-to-end orchestrator: load, split, train, validate, test and publish.</summary>
public sealed class TrainingPipeline
{
    private readonly MLContext _mlContext;
    private readonly OuladDataLoader _loader;
    private readonly FeatureEngineering _features;
    private readonly ModelTrainer _trainer;
    private readonly ModelEvaluator _evaluator;

    public TrainingPipeline(
        MLContext mlContext,
        OuladDataLoader loader,
        FeatureEngineering features,
        ModelTrainer trainer,
        ModelEvaluator evaluator)
    {
        _mlContext = mlContext;
        _loader = loader;
        _features = features;
        _trainer = trainer;
        _evaluator = evaluator;
    }

    /// <summary>Compatibility entry point returning untouched test metrics.</summary>
    public ModelMetrics Run(string dataPath, string modelOutputPath)
        => Run(new TrainingOptions
        {
            DataDirectory = dataPath,
            ModelOutputPath = modelOutputPath
        }).Report.TestMetrics;

    public TrainingResult Run(TrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var modelPath = Path.GetFullPath(options.ModelOutputPath);
        var metadataPath = Path.GetFullPath(options.ResolveMetadataPath());
        var reportPath = Path.GetFullPath(options.ResolveReportPath());
        EnsureDistinctTargets(modelPath, metadataPath, reportPath);

        var trainedAtUtc = DateTime.UtcNow;
        var modelVersion = string.IsNullOrWhiteSpace(options.ModelVersion)
            ? $"{StudentActivityFeatures.SchemaVersion}-{trainedAtUtc:yyyyMMddTHHmmssfffZ}"
            : options.ModelVersion.Trim();

        var observations = _loader.LoadObservations(options.DataDirectory);
        var split = GroupDataSplitter.Split(
            observations,
            options.Seed,
            options.TrainingFraction,
            options.ValidationFraction);

        var trainingData = _mlContext.Data.LoadFromEnumerable(split.Training);
        var validationData = _mlContext.Data.LoadFromEnumerable(split.Validation);
        var testData = _mlContext.Data.LoadFromEnumerable(split.Test);

        // Fit preprocessing, normalizer and class-weighted classifier on training rows only.
        var model = _trainer.Train(trainingData, _features.BuildPipeline());
        float threshold;
        try
        {
            threshold = _evaluator.SelectThreshold(
                model,
                validationData,
                options.MinimumRecall,
                options.MinimumPrecision);
        }
        catch (ModelQualityGateException exception)
        {
            var fallbackValidation = _evaluator.Evaluate(model, validationData, 0.5f, modelVersion);
            var fallbackTest = _evaluator.Evaluate(model, testData, 0.5f, modelVersion);
            var failureReport = CreateReport(
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                options,
                split,
                fallbackValidation,
                fallbackTest,
                passed: false,
                modelSha256: string.Empty);
            var failurePath = WriteFailureReport(reportPath, modelVersion, failureReport);
            throw new ModelQualityGateException(exception.Message, failureReport, failurePath, exception);
        }

        var validationMetrics = _evaluator.Evaluate(model, validationData, threshold, modelVersion);
        var testMetrics = _evaluator.Evaluate(model, testData, threshold, modelVersion);
        var passed = testMetrics.AreaUnderRocCurve >= options.MinimumTestAreaUnderRocCurve
                     && testMetrics.Recall >= options.MinimumRecall
                     && testMetrics.Precision >= options.MinimumPrecision;

        if (!passed)
        {
            var failureReport = CreateReport(
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                options,
                split,
                validationMetrics,
                testMetrics,
                passed: false,
                modelSha256: string.Empty);
            var failurePath = WriteFailureReport(reportPath, modelVersion, failureReport);
            throw new ModelQualityGateException(
                $"The untouched test partition failed the quality gate: AUC {testMetrics.AreaUnderRocCurve:F3} " +
                $"(required {options.MinimumTestAreaUnderRocCurve:F2}), recall {testMetrics.Recall:F3} " +
                $"(required {options.MinimumRecall:F2}), precision {testMetrics.Precision:F3} " +
                $"(required {options.MinimumPrecision:F2}).",
                failureReport,
                failurePath);
        }

        var stagedModel = TemporarySibling(modelPath);
        var stagedMetadata = TemporarySibling(metadataPath);
        var stagedReport = TemporarySibling(reportPath);
        try
        {
            _trainer.Save(model, trainingData.Schema, stagedModel);
            VerifyReloadParity(model, stagedModel, testData);
            var modelSha256 = FileHash.ComputeSha256(stagedModel);

            var metadata = new RiskModelMetadata
            {
                SchemaVersion = StudentActivityFeatures.SchemaVersion,
                ModelVersion = modelVersion,
                FeatureNames = StudentActivityFeatures.FeatureNames.ToList(),
                DecisionThreshold = threshold,
                TrainedAtUtc = trainedAtUtc,
                Seed = options.Seed,
                ObservationWindowDays = OuladDataLoader.ObservationWindowDays,
                PredictionWindowDays = OuladDataLoader.PredictionWindowDays,
                ObservationStrideDays = OuladDataLoader.ObservationStrideDays,
                TrainingStudentCount = split.TrainingStudents.Count,
                ValidationStudentCount = split.ValidationStudents.Count,
                TestStudentCount = split.TestStudents.Count,
                TrainingRowCount = split.Training.Count,
                ValidationRowCount = split.Validation.Count,
                TestRowCount = split.Test.Count,
                ModelSha256 = modelSha256,
                SourceUrl = options.SourceUrl,
                SourceSha256 = NormalizeHash(options.SourceSha256),
                ValidationMetrics = validationMetrics,
                TestMetrics = testMetrics
            };
            var report = CreateReport(
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                options,
                split,
                validationMetrics,
                testMetrics,
                passed: true,
                modelSha256);

            WriteJson(stagedMetadata, metadata);
            WriteJson(stagedReport, report);
            ArtifactPublisher.Publish(
                (stagedModel, modelPath),
                (stagedMetadata, metadataPath),
                (stagedReport, reportPath));

            return new TrainingResult(metadata, report, modelPath, metadataPath, reportPath);
        }
        finally
        {
            DeleteIfExists(stagedModel);
            DeleteIfExists(stagedMetadata);
            DeleteIfExists(stagedReport);
        }
    }

    private void VerifyReloadParity(ITransformer original, string savedPath, IDataView testData)
    {
        var reloaded = _mlContext.Model.Load(savedPath, out _);
        var expected = _evaluator.Score(original, testData).Take(100).ToArray();
        var actual = _evaluator.Score(reloaded, testData).Take(100).ToArray();
        if (expected.Length != actual.Length)
            throw new InvalidDataException("Reloaded risk model returned a different number of verification rows.");

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].Label != actual[index].Label
                || Math.Abs(expected[index].Probability - actual[index].Probability) > 1e-6f
                || Math.Abs(expected[index].Score - actual[index].Score) > 1e-6f)
            {
                throw new InvalidDataException(
                    $"Reloaded risk model failed prediction parity at verification row {index + 1}.");
            }
        }
    }

    private static TrainingReport CreateReport(
        string modelVersion,
        DateTime trainedAtUtc,
        string modelPath,
        string metadataPath,
        TrainingOptions options,
        GroupedDatasetSplit split,
        ModelMetrics validation,
        ModelMetrics test,
        bool passed,
        string modelSha256)
        => new()
        {
            ModelVersion = modelVersion,
            TrainedAtUtc = trainedAtUtc,
            ModelPath = modelPath,
            MetadataPath = metadataPath,
            ModelSha256 = modelSha256,
            SourceUrl = options.SourceUrl,
            SourceSha256 = NormalizeHash(options.SourceSha256),
            Split = new DatasetSplitSummary
            {
                TrainingStudents = split.TrainingStudents.Count,
                ValidationStudents = split.ValidationStudents.Count,
                TestStudents = split.TestStudents.Count,
                TrainingRows = split.Training.Count,
                ValidationRows = split.Validation.Count,
                TestRows = split.Test.Count
            },
            ValidationMetrics = validation,
            TestMetrics = test,
            QualityGate = new QualityGateResult
            {
                MinimumAreaUnderRocCurve = options.MinimumTestAreaUnderRocCurve,
                MinimumRecall = options.MinimumRecall,
                MinimumPrecision = options.MinimumPrecision,
                Passed = passed
            }
        };

    private static string WriteFailureReport(string reportPath, string modelVersion, TrainingReport report)
    {
        var directory = Path.GetDirectoryName(reportPath) ?? Directory.GetCurrentDirectory();
        var fileName = Path.GetFileNameWithoutExtension(reportPath);
        var extension = Path.GetExtension(reportPath);
        var safeVersion = string.Concat(modelVersion.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var failurePath = Path.Combine(directory, $"{fileName}.failed-{safeVersion}{extension}");
        var stagedFailure = TemporarySibling(failurePath);
        try
        {
            WriteJson(stagedFailure, report);
            Directory.CreateDirectory(directory);
            File.Move(stagedFailure, failurePath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(stagedFailure);
        }

        return failurePath;
    }

    private static void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, ArtifactJson.Options);
        stream.Flush(flushToDisk: true);
    }

    private static string TemporarySibling(string target)
    {
        var fullTarget = Path.GetFullPath(target);
        var directory = Path.GetDirectoryName(fullTarget)
            ?? throw new InvalidOperationException("Artifact target has no parent directory.");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
    }

    private static void EnsureDistinctTargets(params string[] paths)
    {
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("Model, metadata and report output paths must be distinct.");
    }

    private static string? NormalizeHash(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

internal static class ArtifactPublisher
{
    public static void Publish(params (string StagedPath, string TargetPath)[] artifacts)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var backups = new List<(string Target, string Backup)>();
        var published = new List<string>();
        var committed = false;
        try
        {
            foreach (var (_, target) in artifacts)
            {
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                if (!File.Exists(target))
                    continue;

                var backup = $"{target}.{transactionId}.backup";
                File.Move(target, backup);
                backups.Add((target, backup));
            }

            foreach (var (staged, target) in artifacts)
            {
                File.Move(staged, target);
                published.Add(target);
            }

            committed = true;
        }
        catch
        {
            foreach (var target in published.AsEnumerable().Reverse())
            {
                if (File.Exists(target))
                    File.Delete(target);
            }
            foreach (var (target, backup) in backups.AsEnumerable().Reverse())
            {
                if (File.Exists(backup))
                    File.Move(backup, target, overwrite: true);
            }
            throw;
        }
        finally
        {
            if (committed)
            {
                // Cleanup is outside the transaction boundary. Once every staged artifact has
                // reached its target, a backup deletion failure must never roll back (and lose)
                // an otherwise valid publication.
                foreach (var (_, backup) in backups)
                {
                    try
                    {
                        File.Delete(backup);
                    }
                    catch (IOException)
                    {
                        // A stale uniquely named backup is safe and can be removed manually.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // See above; published artifacts remain the authoritative set.
                    }
                }
            }
        }
    }
}
