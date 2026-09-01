using Lodestone.ML.Models;

namespace Lodestone.ML.Training;

public sealed record GroupedDatasetSplit(
    IReadOnlyList<StudentActivityObservation> Training,
    IReadOnlyList<StudentActivityObservation> Validation,
    IReadOnlyList<StudentActivityObservation> Test,
    IReadOnlySet<string> TrainingStudents,
    IReadOnlySet<string> ValidationStudents,
    IReadOnlySet<string> TestStudents);

/// <summary>
/// Deterministically splits global students, never enrollment rows, across datasets. Students
/// are stratified by whether any of their rolling observations is positive so all three held-out
/// partitions contain both classes and no student's observations can leak between partitions.
/// </summary>
public static class GroupDataSplitter
{
    public static GroupedDatasetSplit Split(
        IReadOnlyList<StudentActivityObservation> observations,
        int seed = 42,
        double trainingFraction = 0.70,
        double validationFraction = 0.15)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
            throw new InvalidDataException("The dataset contains no observations.");

        if (!double.IsFinite(trainingFraction) || !double.IsFinite(validationFraction)
            || trainingFraction <= 0 || validationFraction <= 0
            || trainingFraction + validationFraction >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trainingFraction),
                "Training and validation fractions must be positive and leave a positive test fraction.");
        }

        if (observations.Any(row => string.IsNullOrWhiteSpace(row.StudentGroupKey)))
            throw new InvalidDataException("Every observation must contain a non-empty global student group key.");

        var studentsByClass = observations
            .GroupBy(row => row.StudentGroupKey, StringComparer.Ordinal)
            .Select(group => new StudentClass(group.Key, group.Any(row => row.IsAtRisk)))
            .GroupBy(student => student.IsPositive)
            .ToDictionary(group => group.Key, group => group.Select(student => student.Key).ToArray());

        var positiveStudents = studentsByClass.GetValueOrDefault(true) ?? [];
        var negativeStudents = studentsByClass.GetValueOrDefault(false) ?? [];
        if (positiveStudents.Length < 3 || negativeStudents.Length < 3)
        {
            throw new InvalidDataException(
                "At least three positive and three negative global students are required for a stratified train/validation/test split.");
        }

        var trainingStudents = new HashSet<string>(StringComparer.Ordinal);
        var validationStudents = new HashSet<string>(StringComparer.Ordinal);
        var testStudents = new HashSet<string>(StringComparer.Ordinal);
        AssignStratum(positiveStudents, seed, trainingFraction, validationFraction,
            trainingStudents, validationStudents, testStudents);
        // Use a decorrelated seed for the second stratum while retaining repeatability.
        AssignStratum(negativeStudents, unchecked(seed * 397) ^ 0x51ED270B, trainingFraction, validationFraction,
            trainingStudents, validationStudents, testStudents);

        var training = observations.Where(row => trainingStudents.Contains(row.StudentGroupKey)).Select(Clone).ToArray();
        var validation = observations.Where(row => validationStudents.Contains(row.StudentGroupKey)).Select(Clone).ToArray();
        var test = observations.Where(row => testStudents.Contains(row.StudentGroupKey)).Select(Clone).ToArray();
        ApplyBalancedClassWeights(training);

        return new GroupedDatasetSplit(
            training,
            validation,
            test,
            trainingStudents,
            validationStudents,
            testStudents);
    }

    private static void AssignStratum(
        IReadOnlyCollection<string> source,
        int seed,
        double trainingFraction,
        double validationFraction,
        ISet<string> training,
        ISet<string> validation,
        ISet<string> test)
    {
        var students = source.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var random = new Random(seed);
        for (var index = students.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (students[index], students[swap]) = (students[swap], students[index]);
        }

        var trainingCount = Math.Max(1, (int)Math.Floor(students.Length * trainingFraction));
        var validationCount = Math.Max(1, (int)Math.Floor(students.Length * validationFraction));
        var testCount = students.Length - trainingCount - validationCount;
        while (testCount < 1)
        {
            if (trainingCount >= validationCount && trainingCount > 1)
                trainingCount--;
            else if (validationCount > 1)
                validationCount--;
            else
                throw new InvalidDataException("The grouped split produced an empty partition; provide more students.");
            testCount = students.Length - trainingCount - validationCount;
        }

        foreach (var student in students.Take(trainingCount)) training.Add(student);
        foreach (var student in students.Skip(trainingCount).Take(validationCount)) validation.Add(student);
        foreach (var student in students.Skip(trainingCount + validationCount)) test.Add(student);
    }

    private static void ApplyBalancedClassWeights(IReadOnlyList<StudentActivityObservation> rows)
    {
        var positives = rows.Count(row => row.IsAtRisk);
        var negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0)
            throw new InvalidDataException("The training partition must contain both withdrawal and non-withdrawal observations.");

        var positiveWeight = rows.Count / (2f * positives);
        var negativeWeight = rows.Count / (2f * negatives);
        foreach (var row in rows)
            row.ExampleWeight = row.IsAtRisk ? positiveWeight : negativeWeight;
    }

    private static StudentActivityObservation Clone(StudentActivityObservation value) => new()
    {
        ActiveDayRate = value.ActiveDayRate,
        ActivitySpanDays = value.ActivitySpanDays,
        DaysSinceLastAccess = value.DaysSinceLastAccess,
        ForumInteractionCount = value.ForumInteractionCount,
        CourseInteractionCount = value.CourseInteractionCount,
        LateOrMissingAssignmentCount = value.LateOrMissingAssignmentCount,
        RecentActiveDayRate = value.RecentActiveDayRate,
        PriorActiveDayRate = value.PriorActiveDayRate,
        ActiveDayRateTrend = value.ActiveDayRateTrend,
        RecentCourseClickRate = value.RecentCourseClickRate,
        PriorCourseClickRate = value.PriorCourseClickRate,
        CourseClickRateTrend = value.CourseClickRateTrend,
        InactivityStreakDays = value.InactivityStreakDays,
        AssessmentDueRate = value.AssessmentDueRate,
        AssessmentOnTimeRate = value.AssessmentOnTimeRate,
        AssessmentLateOrMissingRate = value.AssessmentLateOrMissingRate,
        CourseProgressRatio = value.CourseProgressRatio,
        CohortActivityPercentile = value.CohortActivityPercentile,
        IsAtRisk = value.IsAtRisk,
        ExampleWeight = value.ExampleWeight,
        StudentGroupKey = value.StudentGroupKey,
        EnrollmentKey = value.EnrollmentKey,
        ObservationDay = value.ObservationDay,
        CoursePresentationKey = value.CoursePresentationKey,
        WithdrawalDay = value.WithdrawalDay
    };

    private sealed record StudentClass(string Key, bool IsPositive);
}
