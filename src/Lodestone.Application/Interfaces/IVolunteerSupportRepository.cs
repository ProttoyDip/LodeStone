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
    Task<IReadOnlyList<SupportRequest>> GetRequestsForStudentAsync(string studentUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveCounselorUserIdsAsync(CancellationToken cancellationToken = default);
}
