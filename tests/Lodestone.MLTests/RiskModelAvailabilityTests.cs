using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.ML;
using Lodestone.ML.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Lodestone.MLTests;

public sealed class RiskModelAvailabilityTests
{
    [Fact]
    public void Disabled_configuration_is_healthy_but_refuses_scoring()
    {
        using var output = new TemporaryDirectory();
        var configuration = new StubConfiguration(new Dictionary<string, string?>
        {
            ["MachineLearning:Enabled"] = "false",
            ["MachineLearning:ModelPath"] = "models/risk-model.zip"
        });

        using var provider = new ServiceCollection()
            .AddMachineLearning(configuration, output.Path)
            .BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsEnabled.Should().BeFalse();
        status.IsAvailable.Should().BeFalse();
        status.IsHealthy.Should().BeTrue();
        status.UnavailableReason.Should().Be("Machine learning is disabled by configuration.");
        var predictor = provider.GetRequiredService<IRiskModelPredictor>();
        var act = () => predictor.Descriptor;
        act.Should().Throw<InvalidOperationException>().WithMessage("*disabled by configuration*");
        provider.GetServices<IRiskModelPredictor>().Should().ContainSingle();
    }

    [Fact]
    public void Enabled_configuration_with_a_missing_artifact_is_unhealthy_and_fails_closed()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "missing.zip");
        var configuration = new StubConfiguration(new Dictionary<string, string?>
        {
            ["MachineLearning:Enabled"] = "true",
            ["MachineLearning:ModelPath"] = Path.GetFileName(modelPath)
        });

        using var provider = new ServiceCollection()
            .AddMachineLearning(configuration, output.Path)
            .BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsEnabled.Should().BeTrue();
        status.IsAvailable.Should().BeFalse();
        status.IsHealthy.Should().BeFalse();
        status.UnavailableReason.Should().Be("The configured risk model artifact is missing.");
        status.UnavailableReason.Should().NotContain(output.Path);
        var predictor = provider.GetRequiredService<IRiskModelPredictor>();
        var act = () => predictor.Descriptor;
        act.Should().Throw<InvalidOperationException>().WithMessage("*artifact is missing*");
    }

    [Fact]
    public void Missing_publication_manifest_registers_an_unhealthy_fail_closed_predictor()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        var metadata = ValidMetadata(ComputeSha256(modelPath));
        File.WriteAllText(Path.ChangeExtension(modelPath, ".metadata.json"), JsonSerializer.Serialize(metadata));

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        provider.GetRequiredService<IRiskModelStatusProvider>().Status.UnavailableReason
            .Should().Be("The configured risk model publication manifest is missing.");
    }

    [Fact]
    public void Corrupt_artifact_with_valid_hash_registers_unavailable_without_leaking_paths()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "corrupt.zip");
        File.WriteAllBytes(modelPath, "not-an-mlnet-model"u8.ToArray());
        var metadata = ValidMetadata(ComputeSha256(modelPath));
        metadata.ModelVersion = "corrupt-v1";
        WriteSidecars(modelPath, metadata);

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeFalse();
        status.UnavailableReason.Should().Contain("invalid");
        status.UnavailableReason.Should().NotContain(output.Path);
    }

    [Fact]
    public void Missing_metadata_with_a_non_json_extension_is_reported_as_missing_metadata()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        var configuration = new StubConfiguration(new Dictionary<string, string?>
        {
            ["MachineLearning:Enabled"] = "true",
            ["MachineLearning:ModelPath"] = "model.zip",
            ["MachineLearning:MetadataPath"] = "model.sidecar"
        });

        using var provider = new ServiceCollection()
            .AddMachineLearning(configuration, output.Path)
            .BuildServiceProvider();

        provider.GetRequiredService<IRiskModelStatusProvider>().Status.UnavailableReason
            .Should().Be("The configured risk model metadata is missing.");
    }

    [Fact]
    public void Malformed_metadata_fails_closed_without_leaking_paths()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        File.WriteAllText(Path.ChangeExtension(modelPath, ".metadata.json"), "{ invalid json");

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsEnabled.Should().BeTrue();
        status.IsHealthy.Should().BeFalse();
        status.UnavailableReason.Should().Be("The configured risk model metadata is not valid JSON.");
        status.UnavailableReason.Should().NotContain(output.Path);
        var act = () => provider.GetRequiredService<IRiskModelPredictor>().Predict(new RiskModelInput(0, 0, 28, 0, 0, 1));
        act.Should().Throw<RiskModelUnavailableException>().WithMessage("*metadata is not valid JSON*");
    }

    [Fact]
    public void Hash_mismatch_fails_closed_before_a_model_can_be_loaded()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "tampered.zip");
        File.WriteAllBytes(modelPath, "tampered"u8.ToArray());
        var metadata = ValidMetadata(new string('0', 64));
        WriteSidecars(modelPath, metadata);

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeFalse();
        status.UnavailableReason.Should().Be("Risk model SHA-256 does not match its metadata.");
    }

    [Fact]
    public void Runtime_rejects_a_model_version_longer_than_the_persistence_contract()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "placeholder"u8.ToArray());
        using var stream = File.OpenRead(modelPath);
        var metadata = ValidMetadata(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        metadata.ModelVersion = new string('v', 129);
        File.WriteAllText(Path.ChangeExtension(modelPath, ".metadata.json"), JsonSerializer.Serialize(metadata));

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        provider.GetRequiredService<IRiskModelStatusProvider>().Status.UnavailableReason
            .Should().Be("Risk model metadata model version exceeds 128 characters.");
    }

    [Fact]
    public void Runtime_rejects_metadata_with_the_wrong_observation_stride_before_loading_the_model()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        using var stream = File.OpenRead(modelPath);
        var metadata = ValidMetadata(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        metadata.ObservationStrideDays = 14;
        File.WriteAllText(Path.ChangeExtension(modelPath, ".metadata.json"), JsonSerializer.Serialize(metadata));

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        provider.GetRequiredService<IRiskModelStatusProvider>().Status.UnavailableReason
            .Should().Be("Risk model observation stride must be 7 days.");
    }

    [Fact]
    public void Runtime_rejects_an_unknown_feature_schema_before_loading_the_model()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        var metadata = ValidMetadata(ComputeSha256(modelPath));
        metadata.SchemaVersion = "withdrawal-28d-v999";
        WriteSidecars(modelPath, metadata);

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeFalse();
        status.UnavailableReason.Should().Be("Unsupported risk feature schema 'withdrawal-28d-v999'.");
    }

    [Fact]
    public void Runtime_rejects_incompatible_feature_order_before_loading_the_model()
    {
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "model.zip");
        File.WriteAllBytes(modelPath, "model"u8.ToArray());
        var metadata = ValidMetadata(ComputeSha256(modelPath));
        metadata.FeatureNames.Reverse();
        WriteSidecars(modelPath, metadata);

        using var provider = new ServiceCollection().AddMachineLearning(modelPath).BuildServiceProvider();

        provider.GetRequiredService<IRiskModelStatusProvider>().Status.UnavailableReason
            .Should().Be("Risk model feature names or order do not match the runtime contract.");
    }

    private static RiskModelMetadata ValidMetadata(string modelHash) => new()
    {
        MetadataSchemaVersion = RiskModelMetadata.CurrentMetadataSchemaVersion,
        ModelVersion = "fixture-v1",
        SchemaVersion = RiskFeatureSchema.Withdrawal28DayV1,
        FeatureNames = RiskFeatureSchemas.Withdrawal28DayV1.FeatureNames.ToList(),
        DecisionThreshold = .5f,
        ObservationWindowDays = 28,
        PredictionWindowDays = 28,
        ObservationStrideDays = 7,
        ModelSha256 = modelHash,
        PublicationId = "fixture-publication",
        EligibleForRuntimeIntegration = true,
        ModelAlgorithm = "FastTree",
        ValidationMetrics = PassingMetrics(),
        TestMetrics = PassingMetrics()
    };

    private static ModelMetrics PassingMetrics() => new()
    {
        AreaUnderRocCurve = .80,
        Recall = .80,
        Precision = .80
    };

    private static void WriteSidecars(string modelPath, RiskModelMetadata metadata)
    {
        var metadataPath = Path.ChangeExtension(modelPath, ".metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata));
        var manifest = new RiskModelPublicationManifest
        {
            PublicationId = metadata.PublicationId,
            EligibleForRuntimeIntegration = true,
            PublishedAtUtc = DateTime.UtcNow,
            ModelVersion = metadata.ModelVersion,
            FeatureSchemaVersion = metadata.SchemaVersion,
            ObservationWindowDays = metadata.ObservationWindowDays,
            PredictionWindowDays = metadata.PredictionWindowDays,
            ObservationStrideDays = metadata.ObservationStrideDays,
            FeatureNames = metadata.FeatureNames,
            ModelSha256 = metadata.ModelSha256,
            MetadataSha256 = ComputeSha256(metadataPath),
            ModelAlgorithm = metadata.ModelAlgorithm,
            QualityGate = new QualityGateResult
            {
                MinimumAreaUnderRocCurve = .70,
                MinimumRecall = .70,
                MinimumPrecision = .30,
                ValidationPassed = true,
                TestPassed = true,
                Passed = true
            }
        };
        File.WriteAllText(
            RiskModelPublicationPaths.GetManifestPath(modelPath),
            JsonSerializer.Serialize(manifest));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class StubConfiguration : IConfigurationSection
    {
        private readonly IReadOnlyDictionary<string, string?> _values;

        public StubConfiguration(IReadOnlyDictionary<string, string?> values, string path = "")
        {
            _values = values;
            Path = path;
        }

        public string? this[string key]
        {
            get => _values.GetValueOrDefault(Combine(key));
            set => throw new NotSupportedException();
        }

        public string Key => Path.Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        public string Path { get; }
        public string? Value
        {
            get => _values.GetValueOrDefault(Path);
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => NeverChangeToken.Instance;
        public IConfigurationSection GetSection(string key) => new StubConfiguration(_values, Combine(key));

        private string Combine(string key) => string.IsNullOrEmpty(Path) ? key : $"{Path}:{key}";
    }

    private sealed class NeverChangeToken : IChangeToken
    {
        public static readonly NeverChangeToken Instance = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
            => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
