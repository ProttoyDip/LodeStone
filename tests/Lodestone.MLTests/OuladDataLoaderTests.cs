using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Training;
using Microsoft.ML;
using Xunit;

namespace Lodestone.MLTests;

public sealed class OuladDataLoaderTests
{
    [Fact]
    public void LoadObservations_builds_rolling_leakage_safe_features()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var observations = loader.LoadObservations(dataset.DirectoryPath);

        var first = observations.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 27);
        first.IsAtRisk.Should().BeTrue("withdrawal on day 55 is inside (27, 55]");
        first.ActiveDayRate.Should().BeApproximately(2f / 28f, 0.00001f);
        first.ActivitySpanDays.Should().Be(28, "the span is inclusive and covers days 0 through 27");
        first.DaysSinceLastAccess.Should().Be(0);
        first.ForumInteractionCount.Should().Be(3);
        first.CourseInteractionCount.Should().Be(5, "forum clicks are excluded from course interactions");
        first.LateOrMissingAssignmentCount.Should().Be(1);

        var secondWindow = observations.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 34);
        secondWindow.ActiveDayRate.Should().BeApproximately(1f / 28f, 0.00001f);
        secondWindow.ActivitySpanDays.Should().Be(1, "one active day has an inclusive span of one");
        secondWindow.DaysSinceLastAccess.Should().Be(7);

        var inactive = observations.Single(row => row.StudentGroupKey == "2" && row.ObservationDay == 27);
        inactive.IsAtRisk.Should().BeFalse();
        inactive.ActiveDayRate.Should().Be(0);
        inactive.ActivitySpanDays.Should().Be(0);
        inactive.DaysSinceLastAccess.Should().Be(28);
    }

    [Fact]
    public void LoadObservations_v2_builds_only_anchor_time_behavioral_features()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var observations = loader.LoadObservations(
            dataset.DirectoryPath,
            RiskFeatureSchema.Withdrawal28DayV2);

        var first = observations.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 27);
        first.RecentActiveDayRate.Should().BeApproximately(1f / 14f, .00001f);
        first.PriorActiveDayRate.Should().BeApproximately(1f / 14f, .00001f);
        first.ActiveDayRateTrend.Should().BeApproximately(0, .00001f);
        first.RecentCourseClickRate.Should().BeApproximately(5f / 14f, .00001f);
        first.PriorCourseClickRate.Should().BeApproximately(3f / 14f, .00001f);
        first.CourseClickRateTrend.Should().BeApproximately(2f / 14f, .00001f);
        first.InactivityStreakDays.Should().Be(0, "activity on the anchor day ends the current inactivity streak");
        first.AssessmentDueRate.Should().BeApproximately(1f / 28f, .00001f);
        first.AssessmentOnTimeRate.Should().Be(0);
        first.AssessmentLateOrMissingRate.Should().Be(1);
        first.CourseProgressRatio.Should().BeApproximately(28f / 70f, .00001f);
        first.CohortActivityPercentile.Should().Be(0, "the training-only cohort calibration is applied after splitting");
        first.IsAtRisk.Should().BeTrue("the label looks ahead only to the defined 28-day target window");
    }

    [Fact]
    public void LoadObservations_v4_uses_only_assessment_history_available_by_the_anchor()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentAssessment.csv"),
            "id_assessment,id_student,date_submitted,is_banked,score\n" +
            "1,1,32,0,35\n" +
            "1,2,18,0,80\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var observations = loader.LoadObservations(
            dataset.DirectoryPath,
            RiskFeatureSchema.Withdrawal28DayV4Experiment);

        var beforeSubmission = observations.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 27);
        beforeSubmission.PriorAssessmentsDueCount.Should().Be(1);
        beforeSubmission.PriorAssessmentCompletionRate.Should().Be(0,
            "the submission and its score occur after this anchor and must remain hidden");
        beforeSubmission.PriorAssessmentMeanScore.Should().Be(0);

        var afterSubmission = observations.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 34);
        afterSubmission.PriorAssessmentCompletionRate.Should().Be(1);
        afterSubmission.PriorAssessmentLateRate.Should().Be(1);
        afterSubmission.PriorAssessmentMeanScore.Should().BeApproximately(.35f, .00001f);
        afterSubmission.PriorAssessmentFailRate.Should().Be(1);
        afterSubmission.LastAssessmentScore.Should().BeApproximately(.35f, .00001f);
    }

    [Fact]
    public void LoadObservations_excludes_rows_with_inconsistent_withdrawal_outcomes_and_dates()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.AppendAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "AAA,2014J,3,Withdrawn\n" +
            "AAA,2014J,4,Fail\n");
        File.AppendAllText(
            Path.Combine(dataset.DirectoryPath, "studentRegistration.csv"),
            "AAA,2014J,3,0,\n" +
            "AAA,2014J,4,0,55\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var observations = loader.LoadObservations(dataset.DirectoryPath);

        observations.Should().NotContain(
            row => row.StudentGroupKey == "3" || row.StudentGroupKey == "4");
    }

    [Fact]
    public void Analysis_label_strategies_do_not_change_the_default_near_term_target()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "courses.csv"),
            "code_module,code_presentation,module_presentation_length\nAAA,2014J,120\n");
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "code_module,code_presentation,id_student,final_result\n" +
            "AAA,2014J,1,Withdrawn\n" +
            "AAA,2014J,2,Pass\n" +
            "AAA,2014J,3,Fail\n");
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentRegistration.csv"),
            "code_module,code_presentation,id_student,date_registration,date_unregistration\n" +
            "AAA,2014J,1,0,90\n" +
            "AAA,2014J,2,0,\n" +
            "AAA,2014J,3,0,\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var nearTerm = loader.LoadObservations(
            dataset.DirectoryPath,
            RiskFeatureSchema.Withdrawal28DayV3);
        var eventual = loader.LoadObservations(
            dataset.DirectoryPath,
            RiskFeatureSchema.Withdrawal28DayV3,
            WithdrawalLabelStrategy.EventualWithdrawal);
        var nonCompletion = loader.LoadObservations(
            dataset.DirectoryPath,
            RiskFeatureSchema.Withdrawal28DayV3,
            WithdrawalLabelStrategy.EventualNonCompletion);

        nearTerm.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 27)
            .IsAtRisk.Should().BeFalse();
        eventual.Single(row => row.StudentGroupKey == "1" && row.ObservationDay == 27)
            .IsAtRisk.Should().BeTrue();
        eventual.Should().OnlyContain(row => row.StudentGroupKey != "2" || !row.IsAtRisk);
        nearTerm.Should().OnlyContain(row => row.StudentGroupKey != "3" || !row.IsAtRisk);
        eventual.Should().OnlyContain(row => row.StudentGroupKey != "3" || !row.IsAtRisk);
        nonCompletion.Single(row => row.StudentGroupKey == "3" && row.ObservationDay == 27)
            .IsAtRisk.Should().BeTrue();
    }

    [Fact]
    public void LoadObservations_rejects_a_missing_canonical_table()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.Delete(Path.Combine(dataset.DirectoryPath, "studentVle.csv"));
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var act = () => loader.LoadObservations(dataset.DirectoryPath);

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*studentVle.csv*");
    }

    [Fact]
    public void LoadObservations_accepts_rfc4180_quoted_fields_with_commas_and_newlines()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "code_module,code_presentation,id_student,final_result,note\r\n" +
            "AAA,2014J,1,Withdrawn,\"With,drawn\"\r\n" +
            "AAA,2014J,2,Pass,\"Pass\r\nwith distinction\"\r\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var observations = loader.LoadObservations(dataset.DirectoryPath);

        observations.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadObservations_rejects_an_unknown_final_result()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "code_module,code_presentation,id_student,final_result\n" +
            "AAA,2014J,1,Unknown\n" +
            "AAA,2014J,2,Pass\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var act = () => loader.LoadObservations(dataset.DirectoryPath);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*final_result*Distinction, Pass, Fail, or Withdrawn*");
    }

    [Fact]
    public void LoadObservations_rejects_characters_after_a_closing_quote()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "code_module,code_presentation,id_student,final_result\n" +
            "AAA,2014J,1,\"Withdrawn\"unexpected\n" +
            "AAA,2014J,2,Pass\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var act = () => loader.LoadObservations(dataset.DirectoryPath);

        act.Should().Throw<IOException>()
            .WithMessage("*characters after a closing quote*");
    }

    [Fact]
    public void LoadObservations_rejects_case_insensitive_duplicate_headers_before_any_rows_are_processed()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.WriteAllText(
            Path.Combine(dataset.DirectoryPath, "studentInfo.csv"),
            "code_module,code_presentation,id_student,ID_STUDENT,final_result\n" +
            "AAA,2014J,1,1,Withdrawn\n" +
            "AAA,2014J,2,2,Pass\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var act = () => loader.LoadObservations(dataset.DirectoryPath);

        act.Should().Throw<IOException>()
            .WithMessage("*studentInfo.csv*duplicate header 'ID_STUDENT'*");
    }

    [Fact]
    public void LoadObservations_rejects_duplicate_student_assessments()
    {
        using var dataset = OuladTestDataset.CreateFeatureSemantics();
        File.AppendAllText(
            Path.Combine(dataset.DirectoryPath, "studentAssessment.csv"),
            "1,1,26,0,35\n");
        var loader = new OuladDataLoader(new MLContext(seed: 42));

        var act = () => loader.LoadObservations(dataset.DirectoryPath);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*duplicate submission*");
    }
}
