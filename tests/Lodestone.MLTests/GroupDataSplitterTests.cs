using FluentAssertions;
using Lodestone.ML.Models;
using Lodestone.ML.Training;
using Xunit;

namespace Lodestone.MLTests;

public sealed class GroupDataSplitterTests
{
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

    private static StudentActivityObservation Observation(int student, bool positive) => new()
    {
        StudentGroupKey = student.ToString(),
        EnrollmentKey = $"AAA/2014J/{student}",
        IsAtRisk = positive
    };
}
