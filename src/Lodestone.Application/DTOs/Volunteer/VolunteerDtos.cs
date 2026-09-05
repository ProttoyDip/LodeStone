using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Volunteer;

public enum VolunteerApprovalState
{
    Pending,
    Approved,
    Rejected,
    Inactive
}

public enum VolunteerAssignmentTarget
{
    Student,
    Group
}

public record CreateVolunteerProfileDto(
    string? Department,
    string? Skills,
    string? Availability,
    string? Bio);

public record VolunteerProfileDto(
    int Id,
    string UserId,
    string FullName,
    string? Department,
    string? Skills,
    string? Availability,
    bool IsApproved,
    bool IsActive,
    string? Bio);

public record AdminVolunteerDto(
    int Id,
    string FullName,
    string? Department,
    string? Skills,
    string? Availability,
    VolunteerApprovalState Status,
    int ActiveAssignments,
    int PendingRequests);

public record AdminVolunteerOverviewDto(
    int TotalVolunteers,
    int PendingApproval,
    int ActiveVolunteers,
    int PendingRequests,
    IReadOnlyList<AdminVolunteerDto> Volunteers);

public record StudentAssignmentOptionDto(
    int StudentProfileId,
    string DisplayName,
    string? Program,
    int EnrollmentYear);

public record StudentGroupOptionDto(
    string Program,
    int EnrollmentYear,
    int StudentCount,
    string DisplayName);

public record VolunteerAssignmentDto(
    int Id,
    int StudentProfileId,
    string StudentDisplayName,
    string? Program,
    int EnrollmentYear,
    string Role,
    string? GroupName,
    string? Notes,
    DateTime AssignedAtUtc);

public record VolunteerAssignmentOptionsDto(
    VolunteerProfileDto Volunteer,
    IReadOnlyList<StudentAssignmentOptionDto> Students,
    IReadOnlyList<StudentGroupOptionDto> Groups,
    IReadOnlyList<VolunteerAssignmentDto> ActiveAssignments);

public record CreateVolunteerAssignmentDto(
    int VolunteerProfileId,
    VolunteerAssignmentTarget Target,
    int? StudentProfileId,
    string? Program,
    int? EnrollmentYear,
    string Role,
    string? Notes);

public record VolunteerAssignmentResultDto(
    int TargetedStudents,
    int CreatedAssignments,
    int ReactivatedAssignments,
    int UpdatedAssignments);

public record CreateSupportRequestDto(
    SupportRequestCategory Category,
    string? Message,
    string? Availability);

public record SupportRequestDto(
    int Id,
    SupportRequestCategory Category,
    string Title,
    string Message,
    string? Availability,
    SupportRequestStatus Status,
    string StudentDisplayName,
    string? VolunteerDisplayName,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? EscalatedAtUtc,
    IReadOnlyList<SupportInteractionDto> Interactions);

public record SupportInteractionDto(
    int Id,
    SupportInteractionType Type,
    string Message,
    bool IsFromVolunteer,
    bool IsCompleted,
    bool EscalatedToCounselor,
    DateTime CreatedAtUtc);

public record VolunteerDashboardDto(
    VolunteerProfileDto? Profile,
    bool CanHandleRequests,
    string? AccessMessage,
    IReadOnlyList<SupportRequestDto> PendingRequests,
    IReadOnlyList<SupportRequestDto> ActiveRequests,
    IReadOnlyList<SupportRequestDto> History);
