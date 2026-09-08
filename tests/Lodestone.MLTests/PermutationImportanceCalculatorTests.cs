using FluentAssertions;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Microsoft.ML;
using Xunit;

namespace Lodestone.MLTests;

public sealed class PermutationImportanceCalculatorTests
{
    [Fact]
    public void Compute_ranks_the_informative_feature_first_and_normalises_shares()
    {
        var ml = new MLContext(seed: 7);
        var random = new Random(7);
        var rows = Enumerable.Range(0, 400)
            .Select(index =>
            {
                var atRisk = index % 2 == 0;
                return new StudentActivityObservation
                {
                    IsAtRisk = atRisk,
                    // Strongly separates the classes.
                    RecentActiveDayRate = atRisk ? 0.1f + (float)random.NextDouble() * 0.2f : 0.7f + (float)random.NextDouble() * 0.2f,
                    // Pure noise.
                    ClickVolatility = (float)random.NextDouble()
                };
            })
            .ToArray();

        var pipeline = ml.Transforms
            .Concatenate("Features", nameof(StudentActivityFeatures.RecentActiveDayRate), nameof(StudentActivityFeatures.ClickVolatility))
            .Append(ml.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: "Label"));
        var model = pipeline.Fit(ml.Data.LoadFromEnumerable(rows));

        var importance = new PermutationImportanceCalculator(ml, new ModelEvaluator(ml)).Compute(
            model,
            rows,
            new[] { nameof(StudentActivityFeatures.RecentActiveDayRate), nameof(StudentActivityFeatures.ClickVolatility) },
            seed: 7,
            permutationCount: 2);

        importance.Should().HaveCount(2);
        importance[0].FeatureName.Should().Be(nameof(StudentActivityFeatures.RecentActiveDayRate));
        importance[0].Rank.Should().Be(1);
        importance[0].MeanAucDrop.Should().BeGreaterThan(0.3, "shuffling the only informative input should wreck the AUC");
        importance[1].MeanAucDrop.Should().BeLessThan(0.05, "shuffling noise should barely move the AUC");
        importance.Sum(entry => entry.Share).Should().BeApproximately(1d, 1e-6);
        importance.Should().OnlyContain(entry => entry.PermutationCount == 2);
    }

    [Fact]
    public void Compute_returns_empty_when_there_is_nothing_to_permute()
    {
        var ml = new MLContext(seed: 1);
        var rows = new[] { new StudentActivityObservation { IsAtRisk = true } };
        var identity = ml.Transforms.CopyColumns("Copy", "Label").Fit(ml.Data.LoadFromEnumerable(rows));

        new PermutationImportanceCalculator(ml, new ModelEvaluator(ml))
            .Compute(identity, rows, new[] { nameof(StudentActivityFeatures.ClickVolatility) }, seed: 1)
            .Should().BeEmpty();
    }
}
