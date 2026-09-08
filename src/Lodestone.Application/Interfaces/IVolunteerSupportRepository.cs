using Lodestone.Domain.Entities;

namespace Lodestone.Application.Interfaces;

public interface IVolunteerSupportRepository
{
    Task<StudentProfile?> GetStudentProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentProfile>> GetStudentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentProfile>> GetStudentsByGroupAsync(string program, int enrollmentYear, CancellationToken cancellationToken = default);
    Task<VolunteerProfile?> GetVolunteerProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<VolunteerProfile?> GetVolunteerProfileByIdAsync(int volunteerProfileId, CancellationToken cancellationToken = default);
    Task CreateVolunteerProfileAsync(VolunteerProfile volunteer, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tracked account for a user, so a volunteer completing their profile can also set the
    /// display name their invitation could not supply.
    /// </summary>
    Task<ApplicationUser?> GetTrackedUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolunteerProfile>> GetAvailableVolunteersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolunteerProfile>> GetVolunteersForAdminAsync(string? query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolunteerAssignment>> GetAssignmentsForVolunteerAsync(int volunteerProfileId, CancellationToken cancellationToken = default);

    /// <summary>Active assignments to approved, active volunteers for one student, volunteer profile and user loaded.</summary>
    Task<IReadOnlyList<VolunteerAssignment>> GetActiveAssignmentsForStudentAsync(int studentProfileId, CancellationToken cancellationToken = default);
    Task<VolunteerAssignment?> GetAssignmentByIdAsync(int assignmentId, CancellationToken cancellationToken = default);
    Task AddVolunteerAssignmentsAsync(IEnumerable<VolunteerAssignment> assignments, CancellationToken cancellationToken = default);

    Task<SupportRequest?> GetRequestByIdAsync(int requestId, CancellationToken cancellationToken = default);
    Task<SupportRequest?> GetRequestForStudentAsync(int requestId, string studentUserId, CancellationToken cancellationToken = default);
    Task<SupportRequest?> GetRequestForVolunteerAsync(int requestId, string volunteerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupportRequest>> GetRequestsForVolunteerAsync(string volunteerUserId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveAssignmentAsync(int volunteerProfileId, int studentProfileId, CancellationToken cancellationToken = default);
    Task<bool> HasVolunteerDeclinedRequestAsync(int requestId, string volunteerUserId, CancellationToken cancellationToken = default);
    Task AddSupportRequestAsync(SupportRequest request, CancellationToken cancellationToken = default);
    Task AddInteractionAsync(SupportInteraction interaction, CancellationToken cancellationToken = default);

    /// <summary>Stamps the viewer's last-read marker on a request without loading the conversation.</summary>
    Task MarkConversationReadAsync(int requestId, bool asVolunteer, DateTime readAtUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupportRequest>> GetRequestsForStudentAsync(string studentUserId, CancellationToken cancellationToken = default);

    /// <summary>Escalated requests no counselor has taken yet, oldest first, with participants loaded.</summary>
    Task<IReadOnlyList<SupportRequest>> GetUnhandledEscalationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pending, unclaimed requests whose student has no active assignment to an approved, active
    /// volunteer. No volunteer can see these, so they wait until an administrator routes them.
    /// </summary>
    Task<IReadOnlyList<SupportRequest>> GetUnroutedPendingRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active assignment counts per approved, active volunteer, as a workload signal.</summary>
    Task<IReadOnlyDictionary<int, int>> GetActiveAssignmentCountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetActiveCounselorUserIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// User identifiers of approved, active volunteers currently assigned to a student. These are
    /// the only volunteers to whom that student's unassigned request is visible, so they are the
    /// only ones worth telling that it changed.
    /// </summary>
    Task<IReadOnlyList<string>> GetAssignedVolunteerUserIdsAsync(
        int studentProfileId,
        CancellationToken cancellationToken = default);
}
