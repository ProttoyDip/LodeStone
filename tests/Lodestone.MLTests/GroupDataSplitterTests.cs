using FluentAssertions;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Xunit;

namespace Lodestone.MLTests;

public sealed class GroupDataSplitterTests
{
    [Fact]
    public void Training_weight_strategies_preserve_the_requested_class_weight_ratio()
    {
        var rows = new[]
        {
            Observation(1, true),
            Observation(2, false),
            Observation(3, false),
            Observation(4, false)
        };

        GroupedCrossValidator.ApplyClassWeights(rows, TrainingWeightStrategy.Balanced);
        (rows[0].ExampleWeight / rows[1].ExampleWeight).Should().BeApproximately(3f, .00001f);

        GroupedCrossValidator.ApplyClassWeights(rows, TrainingWeightStrategy.SquareRootBalanced);
        (rows[0].ExampleWeight / rows[1].ExampleWeight)
            .Should().BeApproximately((float)Math.Sqrt(3), .00001f);

        GroupedCrossValidator.ApplyClassWeights(rows, TrainingWeightStrategy.Unweighted);
        rows.Should().OnlyContain(row => row.ExampleWeight == 1f);
    }

    [Fact]
    public void Split_is_deterministic_stratified_and_keeps_global_students_isolated()
    {
        var observations = Enumerable.Range(1, 24)
            .SelectMany(student => new[]
            {
                Observation(student, student <= 12),
                Observation(student, false)
            })
            .ToArray();

        var first = GroupDataSplitter.Split(observations, seed: 42);
        var second = GroupDataSplitter.Split(observations, seed: 42);
        var reordered = GroupDataSplitter.Split(observations.Reverse().ToArray(), seed: 42);

        first.TrainingStudents.Should().BeEquivalentTo(second.TrainingStudents);
        first.ValidationStudents.Should().BeEquivalentTo(second.ValidationStudents);
        first.TestStudents.Should().BeEquivalentTo(second.TestStudents);
        first.TrainingStudents.Should().BeEquivalentTo(reordered.TrainingStudents);
        first.ValidationStudents.Should().BeEquivalentTo(reordered.ValidationStudents);
        first.TestStudents.Should().BeEquivalentTo(reordered.TestStudents);
        first.TrainingStudents.Should().NotIntersectWith(first.ValidationStudents);
        first.TrainingStudents.Should().NotIntersectWith(first.TestStudents);
        first.ValidationStudents.Should().NotIntersectWith(first.TestStudents);
        first.Training.Should().Contain(row => row.IsAtRisk).And.Contain(row => !row.IsAtRisk);
        first.Validation.Should().Contain(row => row.IsAtRisk).And.Contain(row => !row.IsAtRisk);
        first.Test.Should().Contain(row => row.IsAtRisk).And.Contain(row => !row.IsAtRisk);
        first.Training.Should().Contain(row => row.ExampleWeight != 1f);
        first.Validation.Should().OnlyContain(row => row.ExampleWeight == 1f);
        first.Test.Should().OnlyContain(row => row.ExampleWeight == 1f);
        first.Training.Where(row => row.IsAtRisk).Sum(row => row.ExampleWeight)
            .Should().BeApproximately(first.Training.Count / 2f, .00001f);
        first.Training.Where(row => !row.IsAtRisk).Sum(row => row.ExampleWeight)
            .Should().BeApproximately(first.Training.Count / 2f, .00001f);
    }

    [Fact]
    public void Split_rejects_data_that_cannot_put_both_classes_in_every_partition()
    {
        var observations = Enumerable.Range(1, 5)
            .Select(student => Observation(student, student <= 2))
            .ToArray();

        var act = () => GroupDataSplitter.Split(observations);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*three positive and three negative*");
    }

    [Fact]
    public void Split_can_freeze_student_membership_to_reference_labels()
    {
        var reference = Enumerable.Range(1, 24)
            .Select(student => Observation(student, student <= 12))
            .ToArray();
        var relabeled = Enumerable.Range(1, 24)
            .Select(student => Observation(student, student % 3 == 0))
            .ToArray();

        var baseline = GroupDataSplitter.Split(reference, seed: 42);
        var analysis = GroupDataSplitter.Split(
            relabeled,
            seed: 42,
            stratificationReference: reference);

        analysis.TrainingStudents.Should().BeEquivalentTo(baseline.TrainingStudents);
        analysis.ValidationStudents.Should().BeEquivalentTo(baseline.ValidationStudents);
        analysis.TestStudents.Should().BeEquivalentTo(baseline.TestStudents);
        analysis.Training.Select(row => row.StudentGroupKey)
            .Should().OnlyContain(student => baseline.TrainingStudents.Contains(student));
        analysis.Validation.Select(row => row.StudentGroupKey)
            .Should().OnlyContain(student => baseline.ValidationStudents.Contains(student));
        analysis.Test.Select(row => row.StudentGroupKey)
            .Should().OnlyContain(student => baseline.TestStudents.Contains(student));
    }

    private static StudentActivityObservation Observation(int student, bool positive) => new()
    {
        StudentGroupKey = student.ToString(),
        EnrollmentKey = $"AAA/2014J/{student}",
        IsAtRisk = positive
    };
}
