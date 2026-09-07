using FluentAssertions;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Services;
using Lodestone.Domain.Enums;
using Xunit;

namespace Lodestone.UnitTests.Services;

public sealed class VolunteerMatcherTests
{
    [Fact]
    public void Rank_PutsTheVolunteerWhoseSkillsMatchAtTheTop()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.TechnicalHelp, "I am stuck on a C# assignment and my database code."),
            [
                Candidate(1, "Nadia", skills: "Essay structure, referencing"),
                Candidate(2, "Rahim", skills: "C#, Python, database design"),
                Candidate(3, "Karim", skills: "Campus orientation")
            ]);

        matches[0].FullName.Should().Be("Rahim");
        matches[0].Reasons.Should().Contain(reason => reason.StartsWith("Matches on"));
    }

    [Fact]
    public void Rank_NeverQuotesTheStudentsOwnWordsBackInAReason()
    {
        // The message is read to find matching skills, not to be reproduced in a ranking widget.
        // An administrator choosing a volunteer needs the volunteer's offer, not the student's
        // account of their situation restated as a label.
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.PeerDiscussion,
                "I have been feeling isolated and panicking about failing my degree."),
            [Candidate(1, "Nadia", skills: "Peer listening")]);

        var everyReason = string.Join(" ", matches[0].Reasons).ToLowerInvariant();
        everyReason.Should().NotContain("isolated");
        everyReason.Should().NotContain("panicking");
        everyReason.Should().NotContain("failing");
    }

    [Fact]
    public void Rank_UsesTheCategoryWhenTheStudentWroteVeryLittle()
    {
        // A student in distress may manage one sentence. The category still has to route them.
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.TechnicalHelp, "Please help."),
            [
                Candidate(1, "Nadia", skills: "Revision planning, exam technique"),
                Candidate(2, "Rahim", skills: "Laptop and login problems, software")
            ]);

        matches[0].FullName.Should().Be("Rahim");
    }

    [Fact]
    public void Rank_PrefersAnAvailableVolunteerWhenSkillsAreEqual()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.AcademicGuidance, "Revision help", availability: "Weekday evenings"),
            [
                Candidate(1, "Nadia", skills: "Revision", availability: "Weekend mornings"),
                Candidate(2, "Rahim", skills: "Revision", availability: "Weekday evenings")
            ]);

        matches[0].FullName.Should().Be("Rahim");
        matches[0].Reasons.Should().Contain(reason => reason.StartsWith("Available"));
    }

    [Fact]
    public void Rank_SpreadsWorkWhenTwoVolunteersAreOtherwiseIdentical()
    {
        // One willing volunteer absorbing every request is how volunteers stop volunteering.
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.AcademicGuidance, "Coursework planning"),
            [
                Candidate(1, "Nadia", skills: "Coursework", openAssignments: 4),
                Candidate(2, "Rahim", skills: "Coursework", openAssignments: 0)
            ]);

        matches[0].FullName.Should().Be("Rahim");
    }

    [Fact]
    public void Rank_StillPrefersTheRightSkillsOverAnIdleVolunteer()
    {
        // Capacity must never outrank being able to help. A free volunteer who cannot assist is
        // not a better answer than a busy one who can.
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.TechnicalHelp, "My python code will not run and the portal login fails."),
            [
                Candidate(1, "Nadia", skills: "Flower arranging", openAssignments: 0),
                Candidate(2, "Rahim", skills: "Python, portal, login, software, code", openAssignments: 3)
            ]);

        matches[0].FullName.Should().Be("Rahim");
    }

    [Fact]
    public void Rank_ReturnsEveryoneSoTheAdministratorIsNeverLeftWithNothing()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.GeneralSupport, "Something quite unusual."),
            [Candidate(1, "Nadia", skills: "Chemistry"), Candidate(2, "Rahim", skills: "History")]);

        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(match => match.IsWeak, "nothing in the request matched either volunteer");
    }

    [Fact]
    public void Rank_DoesNotMatchOnWordsThatAppearInEveryRequest()
    {
        // "support" and "student" in a bio would otherwise score against every request ever made,
        // flattening the ranking into noise.
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.GeneralSupport, "I need some support please, I am a student."),
            [Candidate(1, "Nadia", skills: "Supporting students", bio: "I like to help students")]);

        matches[0].Reasons.Should().NotContain(reason => reason.StartsWith("Matches on"));
    }

    [Fact]
    public void Rank_KeepsShortTechnicalTokensThatCarryMeaning()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.TechnicalHelp, "Trouble with c# generics."),
            [Candidate(1, "Nadia", skills: "c#"), Candidate(2, "Rahim", skills: "welding")]);

        matches[0].FullName.Should().Be("Nadia");
        matches[0].Reasons.Should().Contain(reason => reason.Contains("c#"));
    }

    [Fact]
    public void Rank_AlwaysSaysHowMuchWorkAVolunteerAlreadyCarries()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.GeneralSupport, "Anything"),
            [Candidate(1, "Nadia", openAssignments: 0), Candidate(2, "Rahim", openAssignments: 2)]);

        matches.Single(match => match.FullName == "Nadia").Reasons
            .Should().Contain("No students currently assigned");
        matches.Single(match => match.FullName == "Rahim").Reasons
            .Should().Contain("2 students currently assigned");
    }

    [Fact]
    public void Rank_IsStableForVolunteersWhoScoreIdentically()
    {
        var matches = VolunteerMatcher.Rank(
            Request(SupportRequestCategory.GeneralSupport, "Anything"),
            [Candidate(1, "Rahim"), Candidate(2, "Karim"), Candidate(3, "Nadia")]);

        matches.Select(match => match.FullName).Should().ContainInOrder("Karim", "Nadia", "Rahim");
    }

    private static VolunteerMatchRequest Request(
        SupportRequestCategory category,
        string message,
        string? availability = null)
        => new(category, message, availability);

    private static VolunteerMatchCandidate Candidate(
        int id,
        string name,
        string? skills = null,
        string? availability = null,
        string? bio = null,
        int openAssignments = 0)
        => new(id, name, skills, Department: null, bio, availability, openAssignments);
}
