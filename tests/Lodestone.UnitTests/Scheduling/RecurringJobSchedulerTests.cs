using FluentAssertions;
using Hangfire;
using Lodestone.Jobs.Scheduling;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Lodestone.UnitTests.Scheduling;

public sealed class RecurringJobSchedulerTests
{
    private static readonly string[] OptionalJobIds =
    [
        "nudge-dispatch",
        "booking-reminders",
        "forum-moderation",
        "crisis-escalation"
    ];

    [Fact]
    public void Incomplete_jobs_are_removed()
    {
        var recurringJobs = new Mock<IRecurringJobManager>();
        var configuration = new Mock<IConfiguration>();

        RecurringJobScheduler.RegisterRecurringJobs(
            recurringJobs.Object,
            configuration.Object,
            riskScoringEnabled: false);

        InvokedIds(recurringJobs, "AddOrUpdate").Should().BeEmpty();
        InvokedIds(recurringJobs, "RemoveIfExists").Should().BeEquivalentTo(
            OptionalJobIds.Append("weekly-risk-scoring"));
    }

    [Fact]
    public void Incomplete_jobs_are_removed_even_when_legacy_options_are_true()
    {
        var recurringJobs = new Mock<IRecurringJobManager>();
        var configuration = new Mock<IConfiguration>();
        configuration
            .Setup(value => value[It.IsAny<string>()])
            .Returns("true");

        RecurringJobScheduler.RegisterRecurringJobs(
            recurringJobs.Object,
            configuration.Object,
            riskScoringEnabled: false);

        InvokedIds(recurringJobs, "AddOrUpdate").Should().BeEmpty();
        InvokedIds(recurringJobs, "RemoveIfExists").Should().BeEquivalentTo(
            OptionalJobIds.Append("weekly-risk-scoring"));
    }

    private static IReadOnlyList<string> InvokedIds(
        Mock<IRecurringJobManager> recurringJobs,
        string methodName)
        => recurringJobs.Invocations
            .Where(invocation => invocation.Method.Name == methodName)
            .Select(invocation => invocation.Arguments[0] as string)
            .Where(jobId => jobId is not null)
            .Cast<string>()
            .ToArray();
}
