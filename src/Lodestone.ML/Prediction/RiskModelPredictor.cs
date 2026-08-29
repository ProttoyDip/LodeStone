using System.Security.Cryptography;
using System.Text.Json;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.ML;

namespace Lodestone.ML.Prediction;

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
        var features = new StudentActivityFeatures
        {
            ActiveDayRate = input.ActiveDayRate,
            ActivitySpanDays = input.ActivitySpanDays,
            DaysSinceLastAccess = input.DaysSinceLastAccess,
            ForumInteractionCount = input.ForumInteractionCount,
            CourseInteractionCount = input.CourseInteractionCount,
            LateOrMissingAssignmentCount = input.LateOrMissingAssignmentCount
        };

        RiskPrediction prediction;
        // PredictionEngine is deliberately scoped behind a lock. The model is loaded once on
        // startup, never hot-reloaded, so a batch cannot mix model versions.
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
            {
                return new RiskModelLoadResult(
                    new UnavailableRiskModelPredictor("The configured risk model artifact is missing."),
                    RiskModelStatus.Unavailable("The configured risk model artifact is missing."));
            }
            if (!File.Exists(metadataPath))
            {
                return new RiskModelLoadResult(
                    new UnavailableRiskModelPredictor("The configured risk model metadata is missing."),
                    RiskModelStatus.Unavailable("The configured risk model metadata is missing."));
            }

            var metadata = LoadAndValidateMetadata(metadataPath);
            var actualHash = FileHash.ComputeSha256(modelPath);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(metadata.ModelSha256),
                    Convert.FromHexString(actualHash)))
            {
                throw new InvalidDataException("Risk model SHA-256 does not match its metadata.");
            }

            var model = mlContext.Model.Load(modelPath, out _);
            var engine = mlContext.Model.CreatePredictionEngine<StudentActivityFeatures, RiskPrediction>(model);
            // Exercise the complete scoring path before making the artifact available.
            var probe = engine.Predict(new StudentActivityFeatures());
            if (!float.IsFinite(probe.Probability) || probe.Probability is < 0 or > 1)
            {
                engine.Dispose();
                throw new InvalidDataException("The risk model failed its startup prediction probe.");
            }

            var descriptor = new RiskModelDescriptor(
                metadata.ModelVersion,
                metadata.SchemaVersion,
                metadata.ObservationWindowDays,
                metadata.DecisionThreshold);
            var predictor = new LoadedRiskModelPredictor(engine, descriptor);
            return new RiskModelLoadResult(
                predictor,
                RiskModelStatus.Available(metadata.ModelVersion));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var reason = SanitizeLoadFailure(exception);
            return new RiskModelLoadResult(
                new UnavailableRiskModelPredictor(reason),
                RiskModelStatus.Unavailable(reason));
        }
    }

    private static RiskModelMetadata LoadAndValidateMetadata(string path)
    {
        using var stream = File.OpenRead(path);
        var metadata = JsonSerializer.Deserialize<RiskModelMetadata>(stream, ArtifactJson.Options)
            ?? throw new InvalidDataException("Risk model metadata is empty.");

        if (string.IsNullOrWhiteSpace(metadata.ModelVersion))
            throw new InvalidDataException("Risk model metadata has no model version.");
        if (metadata.ModelVersion.Trim().Length > 128)
            throw new InvalidDataException("Risk model metadata model version exceeds 128 characters.");
        if (!string.Equals(metadata.SchemaVersion, StudentActivityFeatures.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported risk feature schema '{metadata.SchemaVersion}'.");
        if (metadata.FeatureNames is null
            || !metadata.FeatureNames.SequenceEqual(StudentActivityFeatures.FeatureNames, StringComparer.Ordinal))
            throw new InvalidDataException("Risk model feature names or order do not match the runtime contract.");
        if (metadata.ObservationWindowDays != RiskFeatureSchema.Withdrawal28DayObservedDays)
            throw new InvalidDataException("Risk model observation window must be 28 days.");
        if (metadata.PredictionWindowDays != OuladDataLoader.PredictionWindowDays)
            throw new InvalidDataException("Risk model prediction window must be 28 days.");
        if (metadata.ObservationStrideDays != OuladDataLoader.ObservationStrideDays)
            throw new InvalidDataException("Risk model observation stride must be 7 days.");
        if (!float.IsFinite(metadata.DecisionThreshold) || metadata.DecisionThreshold is < 0 or > 1)
            throw new InvalidDataException("Risk model decision threshold must be between zero and one.");
        if (string.IsNullOrWhiteSpace(metadata.ModelSha256)
            || metadata.ModelSha256.Length != 64
            || !metadata.ModelSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Risk model metadata contains an invalid SHA-256 value.");

        metadata.ModelVersion = metadata.ModelVersion.Trim();
        return metadata;
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
    public RiskModelDescriptor Descriptor
        => throw new InvalidOperationException($"Risk scoring is unavailable. {reason}");

    public RiskModelPrediction Predict(RiskModelInput input)
        => throw new InvalidOperationException($"Risk scoring is unavailable. {reason}");
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
