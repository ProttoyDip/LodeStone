using FluentAssertions;
using Lodestone.Application.DTOs.Risk;
using Xunit;

namespace Lodestone.MLTests;

public sealed class RiskFeatureVocabularyTests
{
    [Theory]
    [InlineData(RiskFeatureSchema.Withdrawal28DayV1)]
    [InlineData(RiskFeatureSchema.Withdrawal28DayV2)]
    [InlineData(RiskFeatureSchema.Withdrawal28DayV3)]
    public void EveryFeatureInEverySchemaHasAnAgreedPhrase(string schemaVersion)
    {
        // A feature with no phrase falls back to its raw name, which would put "AssessmentMissStreak"
        // in front of a counselor. Adding a feature and forgetting its phrasing should fail here.
        foreach (var feature in RiskFeatureSchemas.GetRequired(schemaVersion).FeatureNames)
        {
            RiskFeatureVocabulary.IsDescribed(feature)
                .Should().BeTrue($"'{feature}' is served to counselors and needs agreed phrasing");
        }
    }

    [Theory]
    [InlineData("attendance")]
    [InlineData("attended")]
    [InlineData("quiz")]
    [InlineData("exam")]
    [InlineData("grade")]
    [InlineData("mark")]
    [InlineData("lecture")]
    [InlineData("class")]
    public void NoPhraseClaimsSomethingTheModelCannotSee(string forbidden)
    {
        // The model reads VLE clickstream and assessment timing. It has no view of physical
        // attendance, no marks, and no timetable. A counselor told "attendance fell" pictures an
        // empty seat and acts on it -- the number cannot support that, so the word must not appear.
        foreach (var feature in RiskFeatureVocabulary.DescribedFeatures)
        {
            RiskFeatureVocabulary.Describe(feature)
                .Should().NotContainEquivalentOf(forbidden,
                    $"'{feature}' would be overstating what was measured");
        }
    }

    [Fact]
    public void AnUnknownFeatureFallsBackToItsNameRatherThanAnInventedMeaning()
        => RiskFeatureVocabulary.Describe("SomeFutureFeature").Should().Be("SomeFutureFeature");

    [Fact]
    public void AnAbsentFeatureNameDescribesNothing()
        => RiskFeatureVocabulary.Describe("   ").Should().BeEmpty();
}
