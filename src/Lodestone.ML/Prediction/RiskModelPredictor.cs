using System.Security.Cryptography;
using System.Text.Json;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.ML;

namespace Lodestone.ML.Prediction;

/// <summary>
/// Startup-loaded, immutable ML.NET predictor. Availability is granted only to a mutually bound
/// model, metadata, and accepted-publication manifest; there is intentionally no reload or
/// fallback artifact path.
/// </summary>
internal sealed class LoadedRiskModelPredictor : IRiskModelPredictor, IDisposable
{
    private readonly PredictionEngine<StudentActivityFeatures, RiskPrediction> _engine;
    private readonly object _gate = new();

    private LoadedRiskModelPredictor(
        PredictionEngine<StudentActivityFeatures, RiskPrediction> engine,
        RiskModelDescriptor descriptor)
    {
        _engine = engine;
        Descriptor = descriptor;
    }

    public RiskModelDescriptor Descriptor { get; }

    public RiskModelPrediction Predict(RiskModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!string.Equals(input.FeatureSchemaVersion, Descriptor.FeatureSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The risk-model input schema does not match the loaded model.");
        }

        var features = MapFeatures(input, Descriptor.FeatureNames);
        RiskPrediction prediction;

        // PredictionEngine is scoped behind a lock. The model is loaded once at startup and
        // never hot-reloaded, so a scheduled batch cannot mix model versions.
        lock (_gate)
            prediction = _engine.Predict(features);

        if (!float.IsFinite(prediction.Probability) || prediction.Probability is < 0 or > 1)
            throw new InvalidOperationException("The loaded risk model produced an invalid probability.");
        return new RiskModelPrediction(prediction.Probability);
    }

    public void Dispose() => _engine.Dispose();

    public static RiskModelLoadResult TryLoad(
        MLContext mlContext,
        string modelPath,
        string metadataPath)
    {
        try
        {
            if (!File.Exists(modelPath))
                return Unavailable("The configured risk model artifact is missing.");
            if (!File.Exists(metadataPath))
                return Unavailable("The configured risk model metadata is missing.");

            var metadata = LoadAndValidateMetadata(metadataPath);
            var manifestPath = RiskModelPublicationPaths.GetManifestPath(modelPath);
            if (!File.Exists(manifestPath))
                return Unavailable("The configured risk model publication manifest is missing.");
            var manifest = LoadAndValidateManifest(manifestPath, metadataPath, metadata);
            var actualHash = FileHash.ComputeSha256(modelPath);
            EnsureEqualHash(metadata.ModelSha256, actualHash, "Risk model SHA-256 does not match its metadata.");
            EnsureEqualHash(manifest.ModelSha256, actualHash, "Risk model SHA-256 does not match its publication manifest.");

            var model = mlContext.Model.Load(modelPath, out _);
            var engine = mlContext.Model.CreatePredictionEngine<StudentActivityFeatures, RiskPrediction>(model);
            try
            {
                // Exercise the complete scoring path before making the artifact available. A
                // zero vector is valid for both registered schemas and exposes incompatible
                // feature mapping/ML.NET serialization before any student data is processed.
                var probe = engine.Predict(new StudentActivityFeatures());
                if (!float.IsFinite(probe.Probability) || probe.Probability is < 0 or > 1)
                    throw new InvalidDataException("The risk model failed its startup prediction probe.");

                var descriptor = new RiskModelDescriptor(
                    metadata.ModelVersion,
                    metadata.SchemaVersion,
                    metadata.ObservationWindowDays,
                    metadata.DecisionThreshold)
                {
                    FeatureNames = metadata.FeatureNames.AsReadOnly(),
                    PublicationId = manifest.PublicationId
                };
                var predictor = new LoadedRiskModelPredictor(engine, descriptor);
                return new RiskModelLoadResult(
                    predictor,
                    RiskModelStatus.Available(
                        metadata.ModelVersion,
                        metadata.SchemaVersion,
                        manifest.PublicationId,
                        manifest.PublishedAtUtc));
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var reason = SanitizeLoadFailure(exception);
            return Unavailable(reason);
        }
    }

    private static RiskModelLoadResult Unavailable(string reason)
        => new(
            new UnavailableRiskModelPredictor(reason),
            RiskModelStatus.Unavailable(reason));

    private static RiskModelMetadata LoadAndValidateMetadata(string path)
    {
        using var stream = File.OpenRead(path);
        var metadata = JsonSerializer.Deserialize<RiskModelMetadata>(stream, ArtifactJson.Options)
            ?? throw new InvalidDataException("Risk model metadata is empty.");

        if (!string.Equals(
                metadata.MetadataSchemaVersion,
                RiskModelMetadata.CurrentMetadataSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Risk model metadata uses an unsupported metadata schema.");
        }
        ValidateCommonContract(
            metadata.ModelVersion,
            metadata.SchemaVersion,
            metadata.FeatureNames,
            metadata.ObservationWindowDays,
            metadata.PredictionWindowDays,
            metadata.ObservationStrideDays,
            metadata.DecisionThreshold,
            metadata.ModelSha256);
        if (string.IsNullOrWhiteSpace(metadata.PublicationId) || metadata.PublicationId.Trim().Length > 80)
            throw new InvalidDataException("Risk model metadata has an invalid publication identifier.");
        if (!metadata.EligibleForRuntimeIntegration)
            throw new InvalidDataException("Risk model metadata is not eligible for runtime integration.");
        if (string.IsNullOrWhiteSpace(metadata.ModelAlgorithm) || metadata.ModelAlgorithm.Trim().Length > 80)
            throw new InvalidDataException("Risk model metadata has an invalid training algorithm.");
        if (!ModelQualityGates.Passes(metadata.ValidationMetrics)
            || !ModelQualityGates.Passes(metadata.TestMetrics))
        {
            throw new InvalidDataException("Risk model metadata does not satisfy the fixed quality gates.");
        }

        metadata.ModelVersion = metadata.ModelVersion.Trim();
        metadata.PublicationId = metadata.PublicationId.Trim();
        metadata.ModelAlgorithm = metadata.ModelAlgorithm.Trim();
        return metadata;
    }

    private static RiskModelPublicationManifest LoadAndValidateManifest(
        string path,
        string metadataPath,
        RiskModelMetadata metadata)
    {
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<RiskModelPublicationManifest>(stream, ArtifactJson.Options)
            ?? throw new InvalidDataException("Risk model publication manifest is empty.");

        if (!string.Equals(
                manifest.ManifestSchemaVersion,
                RiskModelPublicationManifest.CurrentManifestSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Risk model publication manifest uses an unsupported schema.");
        }
        ValidateCommonContract(
            manifest.ModelVersion,
            manifest.FeatureSchemaVersion,
            manifest.FeatureNames,
            manifest.ObservationWindowDays,
            manifest.PredictionWindowDays,
            manifest.ObservationStrideDays,
            metadata.DecisionThreshold,
            manifest.ModelSha256);
        if (string.IsNullOrWhiteSpace(manifest.PublicationId) || manifest.PublicationId.Trim().Length > 80)
            throw new InvalidDataException("Risk model publication manifest has an invalid publication identifier.");
        if (!manifest.EligibleForRuntimeIntegration)
            throw new InvalidDataException("Risk model publication manifest is not eligible for runtime integration.");
        if (!manifest.QualityGate.Passed
            || !manifest.QualityGate.ValidationPassed
            || !manifest.QualityGate.TestPassed
            || manifest.QualityGate.MinimumAreaUnderRocCurve + 1e-12 < ModelQualityGates.MinimumAreaUnderRocCurve
            || manifest.QualityGate.MinimumRecall + 1e-12 < ModelQualityGates.MinimumRecall
            || manifest.QualityGate.MinimumPrecision + 1e-12 < ModelQualityGates.MinimumPrecision)
        {
            throw new InvalidDataException("Risk model publication manifest does not satisfy the fixed quality gates.");
        }
        if (manifest.PublishedAtUtc == default)
            throw new InvalidDataException("Risk model publication manifest has no publication time.");
        if (string.IsNullOrWhiteSpace(manifest.ModelAlgorithm) || manifest.ModelAlgorithm.Trim().Length > 80)
            throw new InvalidDataException("Risk model publication manifest has an invalid training algorithm.");
        if (!string.Equals(manifest.PublicationId, metadata.PublicationId, StringComparison.Ordinal)
            || !string.Equals(manifest.ModelVersion, metadata.ModelVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.FeatureSchemaVersion, metadata.SchemaVersion, StringComparison.Ordinal)
            || !manifest.FeatureNames.SequenceEqual(metadata.FeatureNames, StringComparer.Ordinal)
            || !string.Equals(manifest.ModelSha256, metadata.ModelSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.ModelAlgorithm, metadata.ModelAlgorithm, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Risk model publication manifest does not match its metadata.");
        }

        var metadataHash = FileHash.ComputeSha256(metadataPath);
        EnsureEqualHash(
            manifest.MetadataSha256,
            metadataHash,
            "Risk model metadata SHA-256 does not match its publication manifest.");
        return manifest;
    }

    private static void ValidateCommonContract(
        string? modelVersion,
        string? featureSchemaVersion,
        IReadOnlyList<string>? featureNames,
        int observationWindowDays,
        int predictionWindowDays,
        int observationStrideDays,
        float decisionThreshold,
        string? modelSha256)
    {
        if (string.IsNullOrWhiteSpace(modelVersion))
            throw new InvalidDataException("Risk model metadata has no model version.");
        if (modelVersion.Trim().Length > 128)
            throw new InvalidDataException("Risk model metadata model version exceeds 128 characters.");
        if (!RiskFeatureSchemas.TryGet(featureSchemaVersion, out var schema))
            throw new InvalidDataException($"Unsupported risk feature schema '{featureSchemaVersion}'.");
        if (featureNames is null || !featureNames.SequenceEqual(schema.FeatureNames, StringComparer.Ordinal))
            throw new InvalidDataException("Risk model feature names or order do not match the runtime contract.");
        if (observationWindowDays != schema.ObservedDays)
            throw new InvalidDataException("Risk model observation window must match its feature schema.");
        if (predictionWindowDays != OuladDataLoader.PredictionWindowDays)
            throw new InvalidDataException("Risk model prediction window must be 28 days.");
        if (observationStrideDays != OuladDataLoader.ObservationStrideDays)
            throw new InvalidDataException("Risk model observation stride must be 7 days.");
        if (!float.IsFinite(decisionThreshold) || decisionThreshold is < 0 or > 1)
            throw new InvalidDataException("Risk model decision threshold must be between zero and one.");
        ValidateHash(modelSha256, "Risk model metadata contains an invalid SHA-256 value.");
    }

    private static void EnsureEqualHash(string expected, string actual, string failureMessage)
    {
        ValidateHash(expected, failureMessage);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual)))
        {
            throw new InvalidDataException(failureMessage);
        }
    }

    private static void ValidateHash(string? value, string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || !value.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(failureMessage);
        }
    }

    private static StudentActivityFeatures MapFeatures(
        RiskModelInput input,
        IReadOnlyList<string> featureNames)
    {
        var features = new StudentActivityFeatures();
        foreach (var featureName in featureNames)
        {
            var value = input.GetFeature(featureName);
            switch (featureName)
            {
                case nameof(StudentActivityFeatures.ActiveDayRate): features.ActiveDayRate = value; break;
                case nameof(StudentActivityFeatures.ActivitySpanDays): features.ActivitySpanDays = value; break;
                case nameof(StudentActivityFeatures.DaysSinceLastAccess): features.DaysSinceLastAccess = value; break;
                case nameof(StudentActivityFeatures.ForumInteractionCount): features.ForumInteractionCount = value; break;
                case nameof(StudentActivityFeatures.CourseInteractionCount): features.CourseInteractionCount = value; break;
                case nameof(StudentActivityFeatures.LateOrMissingAssignmentCount): features.LateOrMissingAssignmentCount = value; break;
                case nameof(StudentActivityFeatures.RecentActiveDayRate): features.RecentActiveDayRate = value; break;
                case nameof(StudentActivityFeatures.PriorActiveDayRate): features.PriorActiveDayRate = value; break;
                case nameof(StudentActivityFeatures.ActiveDayRateTrend): features.ActiveDayRateTrend = value; break;
                case nameof(StudentActivityFeatures.RecentCourseClickRate): features.RecentCourseClickRate = value; break;
                case nameof(StudentActivityFeatures.PriorCourseClickRate): features.PriorCourseClickRate = value; break;
                case nameof(StudentActivityFeatures.CourseClickRateTrend): features.CourseClickRateTrend = value; break;
                case nameof(StudentActivityFeatures.InactivityStreakDays): features.InactivityStreakDays = value; break;
                case nameof(StudentActivityFeatures.AssessmentDueRate): features.AssessmentDueRate = value; break;
                case nameof(StudentActivityFeatures.AssessmentOnTimeRate): features.AssessmentOnTimeRate = value; break;
                case nameof(StudentActivityFeatures.AssessmentLateOrMissingRate): features.AssessmentLateOrMissingRate = value; break;
                case nameof(StudentActivityFeatures.CourseProgressRatio): features.CourseProgressRatio = value; break;
                case nameof(StudentActivityFeatures.CohortActivityPercentile): features.CohortActivityPercentile = value; break;
                default:
                    throw new InvalidOperationException("The loaded risk model contains an unsupported feature mapping.");
            }
        }

        return features;
    }

    private static string SanitizeLoadFailure(Exception exception)
        => exception switch
        {
            FileNotFoundException => "The configured risk model artifact is missing.",
            UnauthorizedAccessException => "The configured risk model files cannot be read.",
            JsonException => "The configured risk model metadata is not valid JSON.",
            InvalidDataException invalidData when
                invalidData.Message.StartsWith("Risk model", StringComparison.Ordinal)
                || invalidData.Message.StartsWith("Unsupported risk", StringComparison.Ordinal)
                => invalidData.Message,
            _ => $"The configured risk model artifact is invalid ({exception.GetType().Name})."
        };
}

internal sealed class UnavailableRiskModelPredictor(string reason) : IRiskModelPredictor
{
    public RiskModelDescriptor Descriptor => throw new RiskModelUnavailableException(reason);

    public RiskModelPrediction Predict(RiskModelInput input)
        => throw new RiskModelUnavailableException(reason);
}

internal sealed record RiskModelLoadResult(IRiskModelPredictor Predictor, RiskModelStatus Status);

internal static class FileHash
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal static class ArtifactJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
