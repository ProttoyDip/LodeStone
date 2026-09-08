using Lodestone.Application.DTOs.Volunteer;

namespace Lodestone.Application.Interfaces;

public interface IVolunteerSupportService
{
    Task<VolunteerProfileDto> CreateVolunteerProfileAsync(CreateVolunteerProfileDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VolunteerProfileDto>> GetAvailableVolunteersAsync(CancellationToken cancellationToken = default);

    Task<AdminVolunteerOverviewDto> GetAdminOverviewAsync(string? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pending requests no volunteer can see, each with <c>VolunteerMatcher</c> suggestions. A
    /// recommendation only: assignment still goes through <see cref="AssignVolunteerAsync"/>.
    /// </summary>
    Task<SupportRequestRoutingDto> GetRequestRoutingAsync(CancellationToken cancellationToken = default);
    Task<VolunteerAssignmentOptionsDto?> GetAssignmentOptionsAsync(int volunteerProfileId, CancellationToken cancellationToken = default);
    Task<bool> ApproveVolunteerAsync(int volunteerProfileId, CancellationToken cancellationToken = default);
    Task<bool> RejectVolunteerAsync(int volunteerProfileId, CancellationToken cancellationToken = default);
    Task<bool> SetVolunteerActiveAsync(int volunteerProfileId, bool isActive, CancellationToken cancellationToken = default);
    Task<VolunteerAssignmentResultDto> AssignVolunteerAsync(CreateVolunteerAssignmentDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAssignmentAsync(int volunteerProfileId, int assignmentId, CancellationToken cancellationToken = default);

    Task<SupportRequestDto> CreateSupportRequestAsync(CreateSupportRequestDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupportRequestDto>> GetRequestsForStudentAsync(CancellationToken cancellationToken = default);
    Task<SupportRequestDto?> GetRequestForStudentAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>Volunteers an administrator has assigned to the signed-in student.</summary>
    Task<IReadOnlyList<AssignedVolunteerDto>> GetAssignedVolunteersForStudentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a private conversation with an assigned volunteer without the request/accept round
    /// trip. Returns the id of the request that carries the conversation, reusing an open one.
    /// </summary>
    Task<int> StartConversationWithVolunteerAsync(int volunteerProfileId, CancellationToken cancellationToken = default);

    Task<VolunteerDashboardDto> GetVolunteerDashboardAsync(CancellationToken cancellationToken = default);
    Task<SupportRequestDto?> GetRequestForVolunteerAsync(int requestId, CancellationToken cancellationToken = default);
    Task<bool> AcceptRequestAsync(int requestId, CancellationToken cancellationToken = default);
    Task<bool> RejectRequestAsync(int requestId, CancellationToken cancellationToken = default);
    Task<SupportInteractionDto?> AddInteractionAsync(int requestId, string message, CancellationToken cancellationToken = default);
    Task<bool> CompleteRequestAsync(int requestId, CancellationToken cancellationToken = default);
    Task<bool> EscalateRequestAsync(int requestId, string? message, CancellationToken cancellationToken = default);
}
