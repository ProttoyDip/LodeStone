using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.ML;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Xunit;

namespace Lodestone.MLTests;

public sealed class TrainingPipelineTests
{
    [Fact]
    public void V2_validation_ranking_keeps_every_usable_cross_validated_candidate()
    {
        var first = ModelTrainingCandidate.V2Candidates[0];
        var second = ModelTrainingCandidate.V2Candidates[1];
        var unusable = ModelTrainingCandidate.V2Candidates[2];
        var results = new[]
        {
            new CrossValidationCandidateResult
            {
                CandidateId = second.Id,
                MeanAreaUnderRocCurve = .75,
                MeanAreaUnderPrecisionRecallCurve = .08,
                MeanF1Score = .10,
                IsUsable = true
            },
            new CrossValidationCandidateResult
            {
                CandidateId = first.Id,
                MeanAreaUnderRocCurve = .76,
                MeanAreaUnderPrecisionRecallCurve = .07,
                MeanF1Score = .09,
                IsUsable = true
            },
            new CrossValidationCandidateResult
            {
                CandidateId = unusable.Id,
                MeanAreaUnderRocCurve = .99,
                IsUsable = false
            }
        };

        var ranked = TrainingPipeline.RankCrossValidatedCandidates(
            ModelTrainingCandidate.V2Candidates,
            results);

        ranked.Select(candidate => candidate.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public void Run_rejects_model_versions_that_cannot_round_trip_through_persistence()
    {
        var act = () => CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = "not-read-because-options-fail-first",
            ModelOutputPath = "unused.zip",
            ModelVersion = new string('v', 129)
        });

        act.Should().Throw<ArgumentException>()
            .WithParameterName("ModelVersion")
            .WithMessage("*128*");
    }

    [Fact]
    public void Run_refuses_to_publish_the_offline_only_v4_experiment_schema()
    {
        var act = () => CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = "not-read-because-options-fail-first",
            ModelOutputPath = "unused.zip",
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV4Experiment,
            UseV2Experiment = true
        });

        act.Should().Throw<ArgumentException>()
            .WithParameterName("FeatureSchemaVersion")
            .WithMessage("*validation-only*");
    }

    [Fact]
    public void Run_accepts_a_model_version_at_the_persistence_limit()
    {
        using var dataset = OuladTestDataset.CreateTraining();
        using var output = new TemporaryDirectory();
        var version = new string('v', 128);

        var result = CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = dataset.DirectoryPath,
            ModelOutputPath = Path.Combine(output.Path, "risk-model.zip"),
            ModelVersion = version
        });

        result.Metadata.ModelVersion.Should().Be(version);
    }

    [Fact]
    public void Run_publishes_hash_bound_reloadable_artifacts()
    {
        using var dataset = OuladTestDataset.CreateTraining();
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "risk-model.zip");
        var pipeline = CreatePipeline();

        var result = pipeline.Run(new TrainingOptions
        {
            DataDirectory = dataset.DirectoryPath,
            ModelOutputPath = modelPath,
            ModelVersion = "fixture-v1",
            SourceUrl = "https://example.test/oulad.zip",
            SourceSha256 = new string('a', 64)
        });

        File.Exists(result.ModelPath).Should().BeTrue();
        File.Exists(result.MetadataPath).Should().BeTrue();
        File.Exists(result.PublicationManifestPath).Should().BeTrue();
        File.Exists(result.ReportPath).Should().BeTrue();
        result.Metadata.ModelSha256.Should().Be(ComputeSha256(result.ModelPath));
        result.Metadata.FeatureNames.Should().Equal(StudentActivityFeatures.FeatureNames);
        result.Metadata.DecisionThreshold.Should().BeInRange(0, 1);
        result.Report.QualityGate.Passed.Should().BeTrue();
        result.Report.TestMetrics.Should().NotBeNull();
        result.Report.TestMetrics!.AreaUnderRocCurve.Should().BeGreaterThanOrEqualTo(.70);

        using var provider = new ServiceCollection()
            .AddMachineLearning(result.ModelPath)
            .BuildServiceProvider();
        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeTrue();
        status.ModelVersion.Should().Be("fixture-v1");
        var predictor = provider.GetRequiredService<IRiskModelPredictor>();
        var prediction = predictor.Predict(new RiskModelInput(0, 0, 28, 0, 0, 1));
        prediction.Probability.Should().BeInRange(0, 1);
    }

    [Fact]
    public void Experiment_v2_trains_a_versioned_runtime_capable_artifact_only_after_all_gates_pass()
    {
        using var dataset = OuladTestDataset.CreateTraining();
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "risk-model.zip");

        var result = CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = dataset.DirectoryPath,
            ModelOutputPath = modelPath,
            ModelVersion = "fixture-v2",
            FeatureSchemaVersion = RiskFeatureSchema.Withdrawal28DayV2,
            UseV2Experiment = true,
            ExperimentName = "experiment-v2",
            Seed = 20260831
        });

        result.Metadata.SchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV2);
        result.Metadata.EligibleForRuntimeIntegration.Should().BeTrue();
        result.Report.CrossValidation.Should().HaveCount(ModelTrainingCandidate.V2Candidates.Count);
        result.Report.QualityGate.Passed.Should().BeTrue();
        File.Exists(result.PublicationManifestPath).Should().BeTrue();

        using var provider = new ServiceCollection().AddMachineLearning(result.ModelPath).BuildServiceProvider();
        var status = provider.GetRequiredService<IRiskModelStatusProvider>().Status;
        status.IsAvailable.Should().BeTrue();
        status.FeatureSchemaVersion.Should().Be(RiskFeatureSchema.Withdrawal28DayV2);
        var prediction = provider.GetRequiredService<IRiskModelPredictor>().Predict(
            RiskModelInput.CreateWithdrawal28DayV2(
                .1f, .2f, -.1f, 1, 2, -1, 12, .05f, .4f, .6f, .4f, .2f));
        prediction.Probability.Should().BeInRange(0, 1);
    }

    [Fact]
    public void Run_rejects_failed_test_gate_and_preserves_prior_artifacts()
    {
        using var dataset = OuladTestDataset.CreateTraining(separable: false);
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "risk-model.zip");
        var metadataPath = Path.ChangeExtension(modelPath, ".metadata.json");
        var reportPath = Path.ChangeExtension(modelPath, ".report.json");
        File.WriteAllText(modelPath, "prior-model");
        File.WriteAllText(metadataPath, "prior-metadata");
        File.WriteAllText(reportPath, "prior-report");

        var act = () => CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = dataset.DirectoryPath,
            ModelOutputPath = modelPath,
            ModelVersion = "rejected-v1"
        });

        var exception = act.Should().Throw<ModelQualityGateException>().Which;
        File.ReadAllText(modelPath).Should().Be("prior-model");
        File.ReadAllText(metadataPath).Should().Be("prior-metadata");
        File.ReadAllText(reportPath).Should().Be("prior-report");
        exception.FailureReportPath.Should().NotBeNull();
        File.Exists(exception.FailureReportPath!).Should().BeTrue();
        exception.Report!.QualityGate.Passed.Should().BeFalse();
        exception.Report.TestMetrics.Should().BeNull();
        exception.Report.TestEvaluationStatus.Should().Be("NotEvaluatedValidationGateFailed");
        exception.Report.FeatureDrift.Should().OnlyContain(
            drift => drift.TestPopulationStabilityIndex == null,
            "the locked test population is not examined when validation fails");
    }

    [Fact]
    public void Run_rejects_a_validation_passing_candidate_when_the_locked_test_fails_and_preserves_prior_artifacts()
    {
        using var dataset = OuladTestDataset.CreateTrainingWithInvertedLockedTestSignals();
        using var output = new TemporaryDirectory();
        var modelPath = Path.Combine(output.Path, "risk-model.zip");
        var metadataPath = Path.ChangeExtension(modelPath, ".metadata.json");
        var manifestPath = RiskModelPublicationPaths.GetManifestPath(modelPath);
        var reportPath = Path.ChangeExtension(modelPath, ".report.json");
        File.WriteAllText(modelPath, "prior-model");
        File.WriteAllText(metadataPath, "prior-metadata");
        File.WriteAllText(manifestPath, "prior-manifest");
        File.WriteAllText(reportPath, "prior-report");

        var act = () => CreatePipeline().Run(new TrainingOptions
        {
            DataDirectory = dataset.DirectoryPath,
            ModelOutputPath = modelPath,
            ModelVersion = "locked-test-rejected-v1"
        });

        var exception = act.Should().Throw<ModelQualityGateException>().Which;
        exception.Report!.QualityGate.ValidationPassed.Should().BeTrue();
        exception.Report.QualityGate.TestPassed.Should().BeFalse();
        exception.Report.QualityGate.Passed.Should().BeFalse();
        exception.Report.TestMetrics.Should().NotBeNull();
        exception.Report.TestEvaluationStatus.Should().Be("EvaluatedRejected");
        File.ReadAllText(modelPath).Should().Be("prior-model");
        File.ReadAllText(metadataPath).Should().Be("prior-metadata");
        File.ReadAllText(manifestPath).Should().Be("prior-manifest");
        File.ReadAllText(reportPath).Should().Be("prior-report");
        exception.FailureReportPath.Should().NotBeNull();
        File.Exists(exception.FailureReportPath!).Should().BeTrue();
    }

    [Fact]
    public void ArtifactPublisher_restores_every_prior_target_when_publication_fails_mid_commit()
    {
        using var output = new TemporaryDirectory();
        var targets = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(output.Path, $"target-{index}.txt"))
            .ToArray();
        var staged = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(output.Path, $"staged-{index}.txt"))
            .ToArray();
        for (var index = 0; index < targets.Length; index++)
            File.WriteAllText(targets[index], $"prior-{index + 1}");
        File.WriteAllText(staged[0], "new-1");
        File.WriteAllText(staged[1], "new-2");
        // staged[2] intentionally does not exist, forcing failure after two moves.

        var act = () => ArtifactPublisher.Publish(
            (staged[0], targets[0]),
            (staged[1], targets[1]),
            (staged[2], targets[2]));

        act.Should().Throw<FileNotFoundException>();
        targets.Select(File.ReadAllText).Should().Equal("prior-1", "prior-2", "prior-3");
        Directory.EnumerateFiles(output.Path, "*.backup").Should().BeEmpty();
    }

    private static TrainingPipeline CreatePipeline()
    {
        var ml = new MLContext(seed: 42);
        return new TrainingPipeline(
            ml,
            new OuladDataLoader(ml),
            new FeatureEngineering(ml),
            new global::Lodestone.ML.Training.ModelTrainer(ml),
            new ModelEvaluator(ml));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
