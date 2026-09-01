using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;
using Lodestone.ML.Prediction;
using Microsoft.ML;

namespace Lodestone.ML.Training;

/// <summary>
/// End-to-end quality-gated training. The locked test partition is touched only
/// after a validation-selected candidate satisfies every fixed validation gate.
/// </summary>
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

    /// <summary>Compatibility entry point returning untouched test metrics for a successful run.</summary>
    public ModelMetrics Run(string dataPath, string modelOutputPath)
        => Run(new TrainingOptions
        {
            DataDirectory = dataPath,
            ModelOutputPath = modelOutputPath
        }).Report.TestMetrics
           ?? throw new InvalidOperationException("A successful training run must evaluate the locked test partition.");

    public TrainingResult Run(TrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var schema = RiskFeatureSchemas.GetRequired(options.FeatureSchemaVersion);
        var modelPath = Path.GetFullPath(options.ModelOutputPath);
        var metadataPath = Path.GetFullPath(options.ResolveMetadataPath());
        var manifestPath = RiskModelPublicationPaths.GetManifestPath(modelPath);
        var reportPath = Path.GetFullPath(options.ResolveReportPath());
        EnsureDistinctTargets(modelPath, metadataPath, manifestPath, reportPath);

        var trainedAtUtc = DateTime.UtcNow;
        var modelVersion = string.IsNullOrWhiteSpace(options.ModelVersion)
            ? $"{schema.Version}-{trainedAtUtc:yyyyMMddTHHmmssfffZ}"
            : options.ModelVersion.Trim();
        var observations = _loader.LoadObservations(options.DataDirectory, schema.Version);
        var split = GroupDataSplitter.Split(
            observations,
            options.Seed,
            options.TrainingFraction,
            options.ValidationFraction);

        // The cohort percentile is fit only from the training partition. Applying it to validation
        // and test never adds their observations to the reference distribution.
        CohortFeatureCalibrator? cohortCalibrator = null;
        if (GroupedCrossValidator.UsesCohortCalibration(schema))
        {
            cohortCalibrator = CohortFeatureCalibrator.Fit(split.Training);
            cohortCalibrator.Apply(split.Training);
            cohortCalibrator.Apply(split.Validation);
        }

        // Grouped cross-validation selects a candidate using class-balanced weights (see
        // GroupedCrossValidator.ApplyClassWeights); the final fit below must use the same
        // weighting so the published model matches the candidate that was actually selected.
        if (options.UseV2Experiment)
        {
            GroupedCrossValidator.ApplyClassWeights(split.Training);
        }

        var trainingData = _mlContext.Data.LoadFromEnumerable(split.Training);
        var validationData = _mlContext.Data.LoadFromEnumerable(split.Validation);
        var candidates = options.UseV2Experiment
            ? ModelTrainingCandidate.V2Candidates
            : new[] { ModelTrainingCandidate.V1FastTree };
        var crossValidation = options.UseV2Experiment
            ? new GroupedCrossValidator(_mlContext, _features, _trainer, _evaluator)
                .Evaluate(split.Training, schema, candidates, options.Seed)
            : Array.Empty<CrossValidationCandidateResult>();
        var validationCandidates = options.UseV2Experiment
            ? SelectCrossValidatedCandidate(candidates, crossValidation)
            : candidates;

        var selected = SelectOnValidation(
            validationCandidates,
            trainingData,
            validationData,
            schema,
            modelVersion,
            options);
        if (selected is null)
        {
            // The locked test set has not been transformed, scored, or inspected in this path.
            var report = CreateReport(
                options,
                schema,
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                split,
                selectedValidation: null,
                testMetrics: null,
                testEvaluationStatus: "NotEvaluatedValidationGateFailed",
                crossValidation,
                validationPassed: false,
                testPassed: false,
                modelSha256: string.Empty,
                thresholdCurve: Array.Empty<ThresholdCurvePoint>(),
                includeLockedTestPopulationDrift: false);
            var failurePath = WriteFailureReport(reportPath, modelVersion, report);
            throw new ModelQualityGateException(
                $"No validation candidate satisfies AUC >= {options.MinimumTestAreaUnderRocCurve:F2}, " +
                $"recall >= {options.MinimumRecall:F2}, and precision >= {options.MinimumPrecision:F2}. " +
                "The locked test partition was not evaluated.",
                report,
                failurePath);
        }

        // This is the one and only locked-test evaluation for the validation-selected candidate.
        cohortCalibrator?.Apply(split.Test);
        var testData = _mlContext.Data.LoadFromEnumerable(split.Test);
        var testMetrics = _evaluator.Evaluate(selected.Model, testData, selected.Threshold, modelVersion);
        testMetrics.MeanLeadTimeDays = MeanPositiveLeadTime(split.Test);
        var testPassed = PassesConfiguredGates(testMetrics, options);
        var thresholdCurve = _evaluator.BuildThresholdCurve(selected.Model, validationData);
        if (!testPassed)
        {
            var report = CreateReport(
                options,
                schema,
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                split,
                selected.ValidationMetrics,
                testMetrics,
                "EvaluatedRejected",
                crossValidation,
                validationPassed: true,
                testPassed: false,
                modelSha256: string.Empty,
                thresholdCurve,
                includeLockedTestPopulationDrift: true);
            var failurePath = WriteFailureReport(reportPath, modelVersion, report);
            throw new ModelQualityGateException(
                $"The locked test partition failed the quality gate: AUC {testMetrics.AreaUnderRocCurve:F3} " +
                $"(required {options.MinimumTestAreaUnderRocCurve:F2}), recall {testMetrics.Recall:F3} " +
                $"(required {options.MinimumRecall:F2}), precision {testMetrics.Precision:F3} " +
                $"(required {options.MinimumPrecision:F2}).",
                report,
                failurePath);
        }

        var stagedModel = TemporarySibling(modelPath);
        var stagedMetadata = TemporarySibling(metadataPath);
        var stagedManifest = TemporarySibling(manifestPath);
        var stagedReport = TemporarySibling(reportPath);
        try
        {
            _trainer.Save(selected.Model, trainingData.Schema, stagedModel);
            // Verify serialization with validation data only. The locked test set must not be
            // transformed a second time after its single final evaluation above.
            VerifyReloadParity(selected.Model, stagedModel, validationData);
            var modelSha256 = FileHash.ComputeSha256(stagedModel);
            var publicationId = Guid.NewGuid().ToString("N");
            var metadata = new RiskModelMetadata
            {
                MetadataSchemaVersion = RiskModelMetadata.CurrentMetadataSchemaVersion,
                SchemaVersion = schema.Version,
                ModelVersion = modelVersion,
                FeatureNames = schema.FeatureNames.ToList(),
                DecisionThreshold = selected.Threshold,
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
                PublicationId = publicationId,
                EligibleForRuntimeIntegration = true,
                ModelAlgorithm = selected.Candidate.Algorithm.ToString(),
                Hyperparameters = new Dictionary<string, string>(selected.Candidate.ToReportValues(), StringComparer.Ordinal),
                SourceUrl = options.SourceUrl,
                SourceSha256 = NormalizeHash(options.SourceSha256),
                ValidationMetrics = selected.ValidationMetrics,
                TestMetrics = testMetrics
            };
            WriteJson(stagedMetadata, metadata);
            var metadataSha256 = FileHash.ComputeSha256(stagedMetadata);
            var report = CreateReport(
                options,
                schema,
                modelVersion,
                trainedAtUtc,
                modelPath,
                metadataPath,
                split,
                selected.ValidationMetrics,
                testMetrics,
                "EvaluatedAccepted",
                crossValidation,
                validationPassed: true,
                testPassed: true,
                modelSha256,
                thresholdCurve,
                includeLockedTestPopulationDrift: true);
            var manifest = new RiskModelPublicationManifest
            {
                PublicationId = publicationId,
                EligibleForRuntimeIntegration = true,
                PublishedAtUtc = trainedAtUtc,
                ModelVersion = modelVersion,
                FeatureSchemaVersion = schema.Version,
                ObservationWindowDays = OuladDataLoader.ObservationWindowDays,
                PredictionWindowDays = OuladDataLoader.PredictionWindowDays,
                ObservationStrideDays = OuladDataLoader.ObservationStrideDays,
                FeatureNames = schema.FeatureNames.ToList(),
                ModelSha256 = modelSha256,
                MetadataSha256 = metadataSha256,
                ModelAlgorithm = selected.Candidate.Algorithm.ToString(),
                QualityGate = report.QualityGate
            };
            WriteJson(stagedManifest, manifest);
            WriteJson(stagedReport, report);
            ArtifactPublisher.Publish(
                (stagedModel, modelPath),
                (stagedMetadata, metadataPath),
                (stagedManifest, manifestPath),
                (stagedReport, reportPath));

            return new TrainingResult(metadata, report, modelPath, metadataPath, reportPath, manifestPath);
        }
        finally
        {
            DeleteIfExists(stagedModel);
            DeleteIfExists(stagedMetadata);
            DeleteIfExists(stagedManifest);
            DeleteIfExists(stagedReport);
        }
    }

    private ValidationSelection? SelectOnValidation(
        IReadOnlyList<ModelTrainingCandidate> candidates,
        IDataView trainingData,
        IDataView validationData,
        RiskFeatureSchemaDefinition schema,
        string modelVersion,
        TrainingOptions options)
    {
        ValidationSelection? selected = null;
        foreach (var candidate in candidates)
        {
            ITransformer model;
            try
            {
                model = _trainer.Train(trainingData, _features.BuildPipeline(schema.FeatureNames), candidate);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                continue;
            }
            float threshold;
            try
            {
                // Select against a raised recall floor so the operating point keeps headroom above
                // the gate it must clear on the locked test partition. Without it the F1 optimum
                // sits exactly on the gate and cohort sampling noise decides publication.
                threshold = _evaluator.SelectThreshold(
                    model,
                    validationData,
                    options.MinimumRecall + options.RecallSelectionMargin,
                    options.MinimumPrecision);
            }
            catch (ModelQualityGateException)
            {
                continue;
            }

            var metrics = _evaluator.Evaluate(model, validationData, threshold, modelVersion);
            if (!PassesConfiguredGates(metrics, options)) continue;
            var current = new ValidationSelection(candidate, model, threshold, metrics);
            if (selected is null || IsBetter(current, selected)) selected = current;
        }

        return selected;
    }

    private static IReadOnlyList<ModelTrainingCandidate> SelectCrossValidatedCandidate(
        IReadOnlyList<ModelTrainingCandidate> candidates,
        IReadOnlyList<CrossValidationCandidateResult> results)
    {
        var selectedId = results
            .Where(result => result.IsUsable)
            .OrderByDescending(result => result.MeanAreaUnderRocCurve)
            .ThenByDescending(result => result.MeanAreaUnderPrecisionRecallCurve)
            .ThenByDescending(result => result.MeanF1Score)
            .ThenBy(result => result.CandidateId, StringComparer.Ordinal)
            .Select(result => result.CandidateId)
            .FirstOrDefault();
        if (selectedId is null) return Array.Empty<ModelTrainingCandidate>();

        var candidate = candidates.SingleOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));
        return candidate is null ? Array.Empty<ModelTrainingCandidate>() : [candidate];
    }

    private static bool IsBetter(ValidationSelection candidate, ValidationSelection current)
        => candidate.ValidationMetrics.F1Score > current.ValidationMetrics.F1Score + 1e-12
           || (NearlyEqual(candidate.ValidationMetrics.F1Score, current.ValidationMetrics.F1Score)
               && candidate.ValidationMetrics.Recall > current.ValidationMetrics.Recall + 1e-12)
           || (NearlyEqual(candidate.ValidationMetrics.F1Score, current.ValidationMetrics.F1Score)
               && NearlyEqual(candidate.ValidationMetrics.Recall, current.ValidationMetrics.Recall)
               && candidate.ValidationMetrics.Precision > current.ValidationMetrics.Precision + 1e-12);

    private static bool PassesConfiguredGates(ModelMetrics metrics, TrainingOptions options)
        => ModelQualityGates.Passes(metrics)
           && metrics.AreaUnderRocCurve + 1e-12 >= options.MinimumTestAreaUnderRocCurve
           && metrics.Recall + 1e-12 >= options.MinimumRecall
           && metrics.Precision + 1e-12 >= options.MinimumPrecision;

    private void VerifyReloadParity(ITransformer original, string savedPath, IDataView verificationData)
    {
        var reloaded = _mlContext.Model.Load(savedPath, out _);
        var expected = _evaluator.Score(original, verificationData).Take(100).ToArray();
        var actual = _evaluator.Score(reloaded, verificationData).Take(100).ToArray();
        if (expected.Length != actual.Length)
            throw new InvalidDataException("Reloaded risk model returned a different number of verification rows.");

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index].Label != actual[index].Label
                || Math.Abs(expected[index].Probability - actual[index].Probability) > 1e-6f
                || Math.Abs(expected[index].Score - actual[index].Score) > 1e-6f)
            {
                throw new InvalidDataException($"Reloaded risk model failed prediction parity at verification row {index + 1}.");
            }
        }
    }

    private static TrainingReport CreateReport(
        TrainingOptions options,
        RiskFeatureSchemaDefinition schema,
        string modelVersion,
        DateTime trainedAtUtc,
        string modelPath,
        string metadataPath,
        GroupedDatasetSplit split,
        ModelMetrics? selectedValidation,
        ModelMetrics? testMetrics,
        string testEvaluationStatus,
        IReadOnlyList<CrossValidationCandidateResult> crossValidation,
        bool validationPassed,
        bool testPassed,
        string modelSha256,
        IReadOnlyList<ThresholdCurvePoint> thresholdCurve,
        bool includeLockedTestPopulationDrift)
        => new()
        {
            ExperimentName = options.ExperimentName.Trim(),
            ModelVersion = modelVersion,
            FeatureSchemaVersion = schema.Version,
            TrainedAtUtc = trainedAtUtc,
            ModelPath = modelPath,
            MetadataPath = metadataPath,
            ModelSha256 = modelSha256,
            SourceUrl = options.SourceUrl,
            SourceSha256 = NormalizeHash(options.SourceSha256),
            DatasetProvenance = new DatasetProvenanceSummary
            {
                SourceUrl = options.SourceUrl,
                SourceSha256 = NormalizeHash(options.SourceSha256),
                DatasetDirectoryHash = ComputeDatasetDirectoryHash(options.DataDirectory)
            },
            Split = new DatasetSplitSummary
            {
                TrainingStudents = split.TrainingStudents.Count,
                ValidationStudents = split.ValidationStudents.Count,
                TestStudents = split.TestStudents.Count,
                TrainingRows = split.Training.Count,
                ValidationRows = split.Validation.Count,
                TestRows = split.Test.Count,
                TrainingStudentHash = ComputeStudentHash(split.TrainingStudents),
                ValidationStudentHash = ComputeStudentHash(split.ValidationStudents),
                TestStudentHash = ComputeStudentHash(split.TestStudents),
                RandomSeed = options.Seed
            },
            ValidationMetrics = selectedValidation ?? new ModelMetrics { ModelVersion = modelVersion },
            TestMetrics = testMetrics,
            TestEvaluationStatus = testEvaluationStatus,
            CrossValidation = crossValidation,
            FeatureDrift = CreateFeatureDrift(
                split.Training,
                split.Validation,
                includeLockedTestPopulationDrift ? split.Test : null,
                schema),
            ThresholdCurve = thresholdCurve,
            QualityGate = new QualityGateResult
            {
                MinimumAreaUnderRocCurve = ModelQualityGates.MinimumAreaUnderRocCurve,
                MinimumRecall = ModelQualityGates.MinimumRecall,
                MinimumPrecision = ModelQualityGates.MinimumPrecision,
                ValidationPassed = validationPassed,
                TestPassed = testPassed,
                Passed = validationPassed && testPassed
            }
        };

    private static IReadOnlyList<FeatureDriftSummary> CreateFeatureDrift(
        IReadOnlyList<StudentActivityObservation> training,
        IReadOnlyList<StudentActivityObservation> validation,
        IReadOnlyList<StudentActivityObservation>? test,
        RiskFeatureSchemaDefinition schema)
        => schema.FeatureNames.Select(name => new FeatureDriftSummary
        {
            FeatureName = name,
            ValidationPopulationStabilityIndex = PopulationStabilityIndex(
                training.Select(row => FeatureValue(row, name)).ToArray(),
                validation.Select(row => FeatureValue(row, name)).ToArray()),
            TestPopulationStabilityIndex = test is null
                ? null
                : PopulationStabilityIndex(
                    training.Select(row => FeatureValue(row, name)).ToArray(),
                    test.Select(row => FeatureValue(row, name)).ToArray())
        }).ToArray();

    private static float FeatureValue(StudentActivityObservation row, string name)
        => name switch
        {
            nameof(StudentActivityObservation.ActiveDayRate) => row.ActiveDayRate,
            nameof(StudentActivityObservation.ActivitySpanDays) => row.ActivitySpanDays,
            nameof(StudentActivityObservation.DaysSinceLastAccess) => row.DaysSinceLastAccess,
            nameof(StudentActivityObservation.ForumInteractionCount) => row.ForumInteractionCount,
            nameof(StudentActivityObservation.CourseInteractionCount) => row.CourseInteractionCount,
            nameof(StudentActivityObservation.LateOrMissingAssignmentCount) => row.LateOrMissingAssignmentCount,
            nameof(StudentActivityObservation.RecentActiveDayRate) => row.RecentActiveDayRate,
            nameof(StudentActivityObservation.PriorActiveDayRate) => row.PriorActiveDayRate,
            nameof(StudentActivityObservation.ActiveDayRateTrend) => row.ActiveDayRateTrend,
            nameof(StudentActivityObservation.RecentCourseClickRate) => row.RecentCourseClickRate,
            nameof(StudentActivityObservation.PriorCourseClickRate) => row.PriorCourseClickRate,
            nameof(StudentActivityObservation.CourseClickRateTrend) => row.CourseClickRateTrend,
            nameof(StudentActivityObservation.InactivityStreakDays) => row.InactivityStreakDays,
            nameof(StudentActivityObservation.AssessmentDueRate) => row.AssessmentDueRate,
            nameof(StudentActivityObservation.AssessmentOnTimeRate) => row.AssessmentOnTimeRate,
            nameof(StudentActivityObservation.AssessmentLateOrMissingRate) => row.AssessmentLateOrMissingRate,
            nameof(StudentActivityObservation.CourseProgressRatio) => row.CourseProgressRatio,
            nameof(StudentActivityObservation.CohortActivityPercentile) => row.CohortActivityPercentile,
            nameof(StudentActivityObservation.ActivityTrendAcceleration) => row.ActivityTrendAcceleration,
            nameof(StudentActivityObservation.ClickVolatility) => row.ClickVolatility,
            nameof(StudentActivityObservation.ForumEngagementShare) => row.ForumEngagementShare,
            nameof(StudentActivityObservation.InactiveWeekRate) => row.InactiveWeekRate,
            nameof(StudentActivityObservation.AssessmentMissStreak) => row.AssessmentMissStreak,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unsupported feature name.")
        };

    private static double PopulationStabilityIndex(IReadOnlyList<float> baseline, IReadOnlyList<float> comparison)
    {
        if (baseline.Count == 0 || comparison.Count == 0) return 0d;
        var sorted = baseline.OrderBy(value => value).ToArray();
        var boundaries = Enumerable.Range(1, 9)
            .Select(index => sorted[(int)Math.Floor(index * (sorted.Length - 1d) / 10d)])
            .Distinct()
            .ToArray();
        var baselineBins = BinFractions(baseline, boundaries);
        var comparisonBins = BinFractions(comparison, boundaries);
        return Enumerable.Range(0, baselineBins.Length)
            .Sum(index =>
            {
                const double epsilon = 1e-6;
                var expected = Math.Max(epsilon, baselineBins[index]);
                var actual = Math.Max(epsilon, comparisonBins[index]);
                return (actual - expected) * Math.Log(actual / expected);
            });
    }

    private static double[] BinFractions(IReadOnlyList<float> values, IReadOnlyList<float> boundaries)
    {
        var bins = new double[boundaries.Count + 1];
        foreach (var value in values)
        {
            var index = 0;
            while (index < boundaries.Count && value > boundaries[index]) index++;
            bins[index]++;
        }
        for (var index = 0; index < bins.Length; index++) bins[index] /= values.Count;
        return bins;
    }

    private static double? MeanPositiveLeadTime(IReadOnlyList<StudentActivityObservation> rows)
    {
        var leads = rows.Where(row => row.IsAtRisk && row.WithdrawalDay.HasValue)
            .Select(row => (double)(row.WithdrawalDay!.Value - row.ObservationDay))
            .ToArray();
        return leads.Length == 0 ? null : leads.Average();
    }

    private static string ComputeStudentHash(IEnumerable<string> students)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join("\n", students.OrderBy(value => value, StringComparer.Ordinal))))).ToLowerInvariant();

    private static string ComputeDatasetDirectoryHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            using var stream = File.OpenRead(file);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

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
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
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
            throw new ArgumentException("Model, metadata, publication manifest, and report output paths must be distinct.");
    }

    private static string? NormalizeHash(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 1e-12;

    private sealed record ValidationSelection(
        ModelTrainingCandidate Candidate,
        ITransformer Model,
        float Threshold,
        ModelMetrics ValidationMetrics);
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
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(target)) continue;
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
                if (File.Exists(target)) File.Delete(target);
            foreach (var (target, backup) in backups.AsEnumerable().Reverse())
                if (File.Exists(backup)) File.Move(backup, target, overwrite: true);
            throw;
        }
        finally
        {
            if (committed)
            {
                foreach (var (_, backup) in backups)
                {
                    try { File.Delete(backup); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
