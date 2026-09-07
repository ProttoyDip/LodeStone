using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Volunteer;

/// <summary>
/// What a request needs, reduced to the parts used for routing.
/// </summary>
/// <remarks>
/// <paramref name="Message"/> is the student's own words. It is read to find matching skills and
/// never reproduced in a match reason: an administrator choosing between volunteers needs to know
/// what the volunteer offers, not to have the student's account of their situation quoted back in a
/// ranking widget.
/// </remarks>
public sealed record VolunteerMatchRequest(
    SupportRequestCategory Category,
    string Message,
    string? Availability);

/// <summary>A volunteer being considered, with the workload they already carry.</summary>
public sealed record VolunteerMatchCandidate(
    int VolunteerProfileId,
    string FullName,
    string? Skills,
    string? Department,
    string? Bio,
    string? Availability,
    int OpenAssignmentCount);

/// <summary>
/// One volunteer's suitability for a request, with the reasons that produced it.
/// </summary>
/// <remarks>
/// The score exists to order the list. The reasons exist so an administrator can disagree with the
/// order: a ranking nobody can audit is just an instruction with a number attached, and assignment
/// remains a human decision.
/// </remarks>
public sealed record VolunteerMatch(
    int VolunteerProfileId,
    string FullName,
    double Score,
    IReadOnlyList<string> Reasons)
{
    /// <summary>True when nothing beyond availability to take work supported this match.</summary>
    public bool IsWeak => Score <= 0.2;
}
