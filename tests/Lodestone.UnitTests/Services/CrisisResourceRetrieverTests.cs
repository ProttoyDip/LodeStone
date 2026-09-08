using FluentAssertions;
using Lodestone.Application.Services;
using Lodestone.Domain.Entities;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class CrisisResourceRetrieverTests
{
    private static readonly IReadOnlyList<CrisisResource> Resources =
    [
        Resource(1, "National Suicide & Crisis Lifeline", "Free, confidential support for people in distress. Available 24/7.", "988", emergency: true),
        Resource(2, "Crisis Text Line", "Text HOME to 741741 to connect with a trained Crisis Counselor. Free, 24/7.", "741741", emergency: true),
        Resource(3, "Emergency Services", "For immediate danger to yourself or others, call emergency services.", "911", emergency: true),
        Resource(4, "SAMHSA National Helpline", "Free, confidential mental health and substance use treatment referral. 24/7, 365-day-a-year.", "1-800-662-4357"),
        Resource(5, "Trevor Project (LGBTQ+)", "Crisis intervention and suicide prevention for LGBTQ+ youth.", "1-866-488-7386"),
        Resource(6, "NAMI HelpLine", "Mental health information, support, and local resources from the National Alliance on Mental Illness.", "1-800-950-6264"),
        Resource(7, "Campus Counseling Centre", "Book a session with a Lodestone counselor. Available Mon–Fri 9am–5pm.", null),
    ];

    [Fact]
    public void Rank_FindsAResourceByTheEverydayWordsAPersonWouldUse()
    {
        var matches = CrisisResourceRetriever.Rank("I have been drinking too much and want to stop", Resources);

        matches.Should().NotBeEmpty();
        matches[0].Resource.Title.Should().Be("SAMHSA National Helpline");
    }

    [Fact]
    public void Rank_PrefersTextingWhenSomeoneAsksToText()
    {
        var matches = CrisisResourceRetriever.Rank("I don't want to talk on the phone, can I text someone?", Resources);

        matches[0].Resource.Title.Should().Be("Crisis Text Line");
    }

    [Fact]
    public void Rank_RoutesBookingLanguageToTheCampusCentre()
    {
        var matches = CrisisResourceRetriever.Rank("how do I book a therapist appointment", Resources);

        matches[0].Resource.Title.Should().Be("Campus Counseling Centre");
    }

    [Fact]
    public void Rank_SurfacesTheLgbtqLineForIdentityLanguage()
    {
        var matches = CrisisResourceRetriever.Rank("I'm trans and my family does not accept me", Resources);

        matches.Select(match => match.Resource.Title).Should().Contain("Trevor Project (LGBTQ+)");
        matches[0].Resource.Title.Should().Be("Trevor Project (LGBTQ+)");
    }

    [Fact]
    public void Rank_ReturnsVerbatimResourcesAndOnlyTheirOwnWordsAsReasons()
    {
        var query = "my secret is that I feel hopeless at night";
        var matches = CrisisResourceRetriever.Rank(query, Resources);

        matches.Should().NotBeEmpty();
        foreach (var match in matches)
        {
            Resources.Should().Contain(match.Resource, "results are the stored resources, never rewritten");
            match.MatchedTerms.Should().NotContain("hopeless");
            match.MatchedTerms.Should().NotContain("secret");
        }
    }

    [Fact]
    public void Rank_AssignsNoCategoryToThePerson()
    {
        // The output type is the check: a resource, a score and matched words. Nothing else exists
        // to carry a judgement about who wrote the query.
        typeof(Application.DTOs.Crisis.CrisisResourceMatch).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo("Resource", "Score", "MatchedTerms");
    }

    [Fact]
    public void Rank_ReturnsNothingForEmptyOrStopwordOnlyInput()
    {
        CrisisResourceRetriever.Rank(null, Resources).Should().BeEmpty();
        CrisisResourceRetriever.Rank("   ", Resources).Should().BeEmpty();
        CrisisResourceRetriever.Rank("I am the and", Resources).Should().BeEmpty();
    }

    [Fact]
    public void Rank_IsDeterministic()
    {
        var first = CrisisResourceRetriever.Rank("someone to call late at night", Resources);
        var second = CrisisResourceRetriever.Rank("someone to call late at night", Resources);

        first.Select(match => (match.Resource.Id, match.Score))
            .Should().Equal(second.Select(match => (match.Resource.Id, match.Score)));
    }

    private static CrisisResource Resource(int id, string title, string description, string? phone, bool emergency = false)
        => new()
        {
            Id = id,
            Title = title,
            Description = description,
            PhoneNumber = phone,
            IsEmergency = emergency,
            DisplayOrder = id
        };
}
