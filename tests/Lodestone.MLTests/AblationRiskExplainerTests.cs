using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.ML.Prediction;
using Xunit;

namespace Lodestone.MLTests;

public sealed class AblationRiskExplainerTests
{
    private const string Schema = RiskFeatureSchema.Withdrawal28DayV2;

    [Fact]
    public void Explain_AttributesTheScoreToTheFeatureThatActuallyMovesIt()
    {
        // A model that reads one feature and ignores the rest. The explanation must find it.
        var explainer = ExplainerFor(values => values[6] * 0.05, queueThreshold: .5);
        var input = Input(inactivityStreakDays: 10f);

        var explanation = explainer.Explain(input, Baseline(inactivityStreakDays: 0f));

        explanation.Should().NotBeNull();
        explanation!.RaisingFactors.Should().NotBeEmpty();
        explanation.RaisingFactors[0].FeatureName.Should().Be("InactivityStreakDays");
        explanation.RaisingFactors[0].Contribution.Should().BeApproximately(.5, 1e-6);
    }

    [Fact]
    public void Explain_ReportsAFeatureWorkingInTheStudentsFavourAsProtective()
    {
        // Assessment on-time rate lowers the score in this model; a student above the comparison
        // level should see it credited to them, not listed among reasons they were flagged.
        var explainer = ExplainerFor(values => .6 - values[8] * .4, queueThreshold: .5);
        var input = Input(assessmentOnTimeRate: 1f);

        var explanation = explainer.Explain(input, Baseline(assessmentOnTimeRate: 0.5f))!;

        explanation.ProtectiveFactors.Should().ContainSingle();
        explanation.ProtectiveFactors[0].FeatureName.Should().Be("AssessmentOnTimeRate");
        explanation.ProtectiveFactors[0].Contribution.Should().BeLessThan(0);
        explanation.RaisingFactors.Should().BeEmpty();
    }

    [Fact]
    public void Explain_OmitsAFeatureAlreadySittingAtTheComparisonValue()
    {
        // Such a feature has nothing to say: it did not move the score away from the baseline.
        // Listing it at zero would pad the panel with rows a counselor has to read past.
        var explainer = ExplainerFor(values => values[6] * 0.05, queueThreshold: .5);

        var explanation = explainer.Explain(Input(inactivityStreakDays: 4f), Baseline(inactivityStreakDays: 4f))!;

        explanation.Factors.Should().NotContain(factor => factor.FeatureName == "InactivityStreakDays");
    }

    [Fact]
    public void Explain_SaysWhenOneFactorAloneAccountsForCrossingTheThreshold()
    {
        var explainer = ExplainerFor(values => .2 + values[6] * .05, queueThreshold: .5);

        var explanation = explainer.Explain(Input(inactivityStreakDays: 8f), Baseline(inactivityStreakDays: 0f))!;

        // .6 with the streak, .2 without it: on its own it carries the student over the threshold.
        explanation.BoundaryNote.Should().NotBeNull();
        explanation.BoundaryNote.Should().Contain("would place them below it");
    }

    [Fact]
    public void Explain_RefusesToCreditOneFactorWhenTheScoreStandsWithoutIt()
    {
        // Two features each push the student over on their own. Saying "this one is why" would be
        // false, and would send a counselor after a single number that changes nothing.
        var explainer = ExplainerFor(values => .5 + values[6] * .02 + values[9] * .2, queueThreshold: .5);

        var explanation = explainer.Explain(
            Input(inactivityStreakDays: 10f, assessmentLateOrMissingRate: 1f),
            Baseline(inactivityStreakDays: 0f, assessmentLateOrMissingRate: 0f))!;

        explanation.BoundaryNote.Should().Contain("No one feature explains the score on its own");
    }

    [Fact]
    public void Explain_OffersNoBoundaryNoteForAStudentBelowTheThreshold()
    {
        var explainer = ExplainerFor(_ => .1, queueThreshold: .5);

        var explanation = explainer.Explain(Input(inactivityStreakDays: 3f), Baseline(inactivityStreakDays: 0f))!;

        explanation.BoundaryNote.Should().BeNull();
    }

    [Fact]
    public void Explain_DeclinesRatherThanGuessWhenTheInputSchemaIsNotTheModels()
    {
        var explainer = ExplainerFor(_ => .9, queueThreshold: .5);
        var v1Input = new RiskModelInput(
            RiskFeatureSchema.Withdrawal28DayV1,
            [0f, 0f, 0f, 0f, 0f, 0f]);

        explainer.Explain(v1Input, Baseline()).Should().BeNull();
    }

    [Fact]
    public void Explain_RefusesABaselineBuiltForADifferentSchema()
    {
        var explainer = ExplainerFor(_ => .9, queueThreshold: .5);
        var v1Baseline = RiskExplanationBaseline.FromPopulation(
            RiskFeatureSchema.Withdrawal28DayV1,
            [new RiskModelInput(RiskFeatureSchema.Withdrawal28DayV1, [0f, 0f, 0f, 0f, 0f, 0f])],
            "v1 students");

        // Silently comparing v2 values against v1 reference points would produce numbers that look
        // fine and mean nothing.
        var act = () => explainer.Explain(Input(), v1Baseline);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explain_UsesTheAgreedPhrasingRatherThanTheRawFeatureName()
    {
        var explainer = ExplainerFor(values => values[6] * .05, queueThreshold: .5);

        var explanation = explainer.Explain(Input(inactivityStreakDays: 6f), Baseline(inactivityStreakDays: 0f))!;

        explanation.RaisingFactors[0].DisplayName
            .Should().Be("longest run of consecutive days with no platform activity");
    }

    private static AblationRiskExplainer ExplainerFor(
        Func<IReadOnlyList<float>, double> score,
        double queueThreshold)
        => new(new StubPredictor(score, queueThreshold));

    private static RiskModelInput Input(
        float inactivityStreakDays = 0f,
        float assessmentOnTimeRate = 0f,
        float assessmentLateOrMissingRate = 0f)
        => new(Schema,
        [
            0f, 0f, 0f, 0f, 0f, 0f,
            inactivityStreakDays,
            0f,
            assessmentOnTimeRate,
            assessmentLateOrMissingRate,
            0f, 0f
        ]);

    private static RiskExplanationBaseline Baseline(
        float inactivityStreakDays = 0f,
        float assessmentOnTimeRate = 0f,
        float assessmentLateOrMissingRate = 0f)
        => RiskExplanationBaseline.FromPopulation(
            Schema,
            [Input(inactivityStreakDays, assessmentOnTimeRate, assessmentLateOrMissingRate)],
            "a typical student on the platform");

    private sealed class StubPredictor : IRiskModelPredictor
    {
        private readonly Func<IReadOnlyList<float>, double> _score;

        public StubPredictor(Func<IReadOnlyList<float>, double> score, double queueThreshold)
        {
            _score = score;
            Descriptor = new RiskModelDescriptor("stub-model", Schema, 28, queueThreshold);
        }

        public RiskModelDescriptor Descriptor { get; }

        public RiskModelPrediction Predict(RiskModelInput input)
            => new(Math.Clamp(_score(input.FeatureValues), 0d, 1d));
    }
}
