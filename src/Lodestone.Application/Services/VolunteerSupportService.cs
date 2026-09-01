using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Lodestone.Application.Services;

public sealed class VolunteerSupportService : IVolunteerSupportService
{
    private const int MaximumSearchLength = 100;
    private const int MaximumMessageLength = 2000;
    private const int MaximumAvailabilityLength = 500;
    private const int MaximumRoleLength = 100;
    private const int MaximumNotesLength = 500;

    private readonly IVolunteerSupportRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLog;
    private readonly INotificationService _notificationService;
    private readonly ILogger<VolunteerSupportService> _logger;

    public VolunteerSupportService(
        IVolunteerSupportRepository repository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLog,
        INotificationService notificationService,
        ILogger<VolunteerSupportService> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _auditLog = auditLog;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<VolunteerProfileDto> CreateVolunteerProfileAsync(
        CreateVolunteerProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Volunteer);
        var existing = await _repository.GetVolunteerProfileByUserIdAsync(userId, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException("A volunteer profile already exists for this account.");

        var volunteer = new VolunteerProfile
        {
            UserId = userId,
            Department = NormalizeOptional(dto.Department, 200, nameof(dto.Department)),
            Skills = NormalizeOptional(dto.Skills, 500, nameof(dto.Skills)),
            Availability = NormalizeOptional(dto.Availability, MaximumAvailabilityLength, nameof(dto.Availability)),
            Bio = NormalizeOptional(dto.Bio, MaximumMessageLength, nameof(dto.Bio)),
            IsApproved = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.CreateVolunteerProfileAsync(volunteer, cancellationToken);
        _auditLog.Record(
            "VolunteerProfile.Create",
            nameof(VolunteerProfile),
            details: "Volunteer profile submitted for administrator approval.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapVolunteer(volunteer);
    }

    public async Task<IReadOnlyList<VolunteerProfileDto>> GetAvailableVolunteersAsync(
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        var volunteers = await _repository.GetAvailableVolunteersAsync(cancellationToken);
        return volunteers.Select(MapVolunteer).ToList().AsReadOnly();
    }

    public async Task<AdminVolunteerOverviewDto> GetAdminOverviewAsync(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        var normalizedQuery = NormalizeOptional(query, MaximumSearchLength, nameof(query));
        var allVolunteers = await _repository.GetVolunteersForAdminAsync(null, cancellationToken);
        var visibleVolunteers = normalizedQuery is null
            ? allVolunteers
            : await _repository.GetVolunteersForAdminAsync(normalizedQuery, cancellationToken);

        var items = visibleVolunteers
            .Select(MapAdminVolunteer)
            .ToList()
            .AsReadOnly();

        var allPendingRequests = allVolunteers
            .SelectMany(GetPendingRequestsForVolunteer)
            .Select(request => request.Id)
            .Distinct()
            .Count();

        return new AdminVolunteerOverviewDto(
            TotalVolunteers: allVolunteers.Count,
            PendingApproval: allVolunteers.Count(profile => GetApprovalState(profile) == VolunteerApprovalState.Pending),
            ActiveVolunteers: allVolunteers.Count(profile => GetApprovalState(profile) == VolunteerApprovalState.Approved),
            PendingRequests: allPendingRequests,
            Volunteers: items);
    }

    public async Task<VolunteerAssignmentOptionsDto?> GetAssignmentOptionsAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        ArgumentOutOfRangeException.ThrowIfLessThan(volunteerProfileId, 1);

        var volunteer = await _repository.GetVolunteerProfileByIdAsync(volunteerProfileId, cancellationToken);
        if (volunteer is null) return null;

        var students = await _repository.GetStudentsAsync(cancellationToken);
        var assignments = await _repository.GetAssignmentsForVolunteerAsync(volunteerProfileId, cancellationToken);

        var studentOptions = students
            .Select(student => new StudentAssignmentOptionDto(
                student.Id,
                StudentDisplayName(student),
                student.Program,
                student.EnrollmentYear))
            .ToList()
            .AsReadOnly();

        var groupOptions = students
            .Where(student => !string.IsNullOrWhiteSpace(student.Program) && student.EnrollmentYear > 0)
            .GroupBy(student => new { Program = student.Program!.Trim(), student.EnrollmentYear })
            .OrderBy(group => group.Key.Program)
            .ThenByDescending(group => group.Key.EnrollmentYear)
            .Select(group => new StudentGroupOptionDto(
                group.Key.Program,
                group.Key.EnrollmentYear,
                group.Count(),
                GroupDisplayName(group.Key.Program, group.Key.EnrollmentYear)))
            .ToList()
            .AsReadOnly();

        var assignmentDtos = assignments
            .Where(assignment => assignment.IsActive)
            .Select(MapAssignment)
            .ToList()
            .AsReadOnly();

        return new VolunteerAssignmentOptionsDto(
            MapVolunteer(volunteer),
            studentOptions,
            groupOptions,
            assignmentDtos);
    }

    public Task<bool> ApproveVolunteerAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
        => ReviewVolunteerAsync(volunteerProfileId, approve: true, cancellationToken);

    public Task<bool> RejectVolunteerAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
        => ReviewVolunteerAsync(volunteerProfileId, approve: false, cancellationToken);

    public async Task<bool> SetVolunteerActiveAsync(
        int volunteerProfileId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        ArgumentOutOfRangeException.ThrowIfLessThan(volunteerProfileId, 1);

        var volunteer = await _repository.GetVolunteerProfileByIdAsync(volunteerProfileId, cancellationToken);
        if (volunteer is null) return false;
        if (isActive && !volunteer.IsApproved)
            throw new InvalidOperationException("Only an approved volunteer can be activated.");

        volunteer.IsActive = isActive;
        volunteer.ModifiedAtUtc = DateTime.UtcNow;
        _auditLog.Record(
            isActive ? "VolunteerProfile.Activate" : "VolunteerProfile.Deactivate",
            nameof(VolunteerProfile),
            volunteer.Id.ToString(),
            isActive ? "Volunteer support access activated." : "Volunteer support access deactivated.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<VolunteerAssignmentResultDto> AssignVolunteerAsync(
        CreateVolunteerAssignmentDto dto,
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        ArgumentOutOfRangeException.ThrowIfLessThan(dto.VolunteerProfileId, 1);

        var role = NormalizeRequired(dto.Role, MaximumRoleLength, nameof(dto.Role));
        var notes = NormalizeOptional(dto.Notes, MaximumNotesLength, nameof(dto.Notes));
        var volunteer = await _repository.GetVolunteerProfileByIdAsync(dto.VolunteerProfileId, cancellationToken)
            ?? throw new InvalidOperationException("Volunteer profile not found.");
        if (!volunteer.IsApproved || !volunteer.IsActive || volunteer.User?.IsActive == false)
            throw new InvalidOperationException("The volunteer must be approved and active before assignments can be added.");

        IReadOnlyList<StudentProfile> targetStudents;
        string? groupName = null;
        switch (dto.Target)
        {
            case VolunteerAssignmentTarget.Student:
            {
                if (!dto.StudentProfileId.HasValue || dto.StudentProfileId.Value <= 0)
                    throw new ArgumentException("Select a student for this assignment.", nameof(dto));

                var students = await _repository.GetStudentsAsync(cancellationToken);
                var student = students.FirstOrDefault(candidate => candidate.Id == dto.StudentProfileId.Value)
                    ?? throw new InvalidOperationException("Student profile not found.");
                targetStudents = new[] { student };
                break;
            }
            case VolunteerAssignmentTarget.Group:
            {
                var program = NormalizeRequired(dto.Program, 200, nameof(dto.Program));
                if (!dto.EnrollmentYear.HasValue || dto.EnrollmentYear.Value < 1900 || dto.EnrollmentYear.Value > DateTime.UtcNow.Year + 1)
                    throw new ArgumentException("Select a valid enrollment year.", nameof(dto));

                targetStudents = await _repository.GetStudentsByGroupAsync(
                    program,
                    dto.EnrollmentYear.Value,
                    cancellationToken);
                if (targetStudents.Count == 0)
                    throw new InvalidOperationException("No students match the selected group.");
                groupName = GroupDisplayName(program, dto.EnrollmentYear.Value);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(dto), "Unknown assignment target.");
        }

        var existingAssignments = await _repository.GetAssignmentsForVolunteerAsync(dto.VolunteerProfileId, cancellationToken);
        var existingByStudent = existingAssignments.ToDictionary(assignment => assignment.StudentProfileId);
        var newAssignments = new List<VolunteerAssignment>();
        var reactivated = 0;
        var updated = 0;
        var nowUtc = DateTime.UtcNow;

        foreach (var student in targetStudents.DistinctBy(student => student.Id))
        {
            if (!existingByStudent.TryGetValue(student.Id, out var assignment))
            {
                newAssignments.Add(new VolunteerAssignment
                {
                    VolunteerProfileId = volunteer.Id,
                    StudentProfileId = student.Id,
                    Role = role,
                    GroupName = groupName,
                    Notes = notes,
                    IsActive = true,
                    CreatedAtUtc = nowUtc
                });
                continue;
            }

            if (!assignment.IsActive)
            {
                assignment.IsActive = true;
                reactivated++;
            }
            else
            {
                updated++;
            }

            assignment.Role = role;
            assignment.GroupName = groupName;
            assignment.Notes = notes;
            assignment.ModifiedAtUtc = nowUtc;
        }

        if (newAssignments.Count > 0)
            await _repository.AddVolunteerAssignmentsAsync(newAssignments, cancellationToken);

        _auditLog.Record(
            "VolunteerAssignment.Create",
            nameof(VolunteerAssignment),
            volunteer.Id.ToString(),
            $"Assigned volunteer to {targetStudents.Count} student profile(s) as {role}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new VolunteerAssignmentResultDto(
            targetStudents.Count,
            newAssignments.Count,
            reactivated,
            updated);
    }

    public async Task<bool> DeactivateAssignmentAsync(
        int volunteerProfileId,
        int assignmentId,
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);
        ArgumentOutOfRangeException.ThrowIfLessThan(volunteerProfileId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(assignmentId, 1);

        var assignment = await _repository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (assignment is null || assignment.VolunteerProfileId != volunteerProfileId) return false;
        if (!assignment.IsActive) return true;

        assignment.IsActive = false;
        assignment.ModifiedAtUtc = DateTime.UtcNow;
        _auditLog.Record(
            "VolunteerAssignment.Deactivate",
            nameof(VolunteerAssignment),
            assignment.Id.ToString(),
            "Volunteer-to-student assignment deactivated.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SupportRequestDto> CreateSupportRequestAsync(
        CreateSupportRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Student);
        if (!Enum.IsDefined(dto.Category))
            throw new ArgumentOutOfRangeException(nameof(dto.Category), "Select a valid support category.");

        var student = await _repository.GetStudentProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Student profile not found.");
        var nowUtc = DateTime.UtcNow;
        var request = new SupportRequest
        {
            StudentProfileId = student.Id,
            StudentProfile = student,
            Category = dto.Category,
            Title = CategoryLabel(dto.Category),
            Message = NormalizeOptional(dto.Message, MaximumMessageLength, nameof(dto.Message)) ?? string.Empty,
            Availability = NormalizeOptional(dto.Availability, MaximumAvailabilityLength, nameof(dto.Availability)),
            Status = SupportRequestStatus.Pending,
            IsVisibleToVolunteers = true,
            CreatedAtUtc = nowUtc
        };

        await _repository.AddSupportRequestAsync(request, cancellationToken);
        _auditLog.Record(
            "SupportRequest.Create",
            nameof(SupportRequest),
            details: "Student created a peer-support request.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapRequest(request);
    }

    public async Task<IReadOnlyList<SupportRequestDto>> GetRequestsForStudentAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Student);
        var requests = await _repository.GetRequestsForStudentAsync(userId, cancellationToken);
        return requests
            .OrderByDescending(request => request.CreatedAtUtc)
            .Select(MapRequest)
            .ToList()
            .AsReadOnly();
    }

    public async Task<SupportRequestDto?> GetRequestForStudentAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Student);
        if (requestId <= 0) return null;

        var request = await _repository.GetRequestForStudentAsync(requestId, userId, cancellationToken);
        return request is null ? null : MapRequest(request);
    }

    public async Task<VolunteerDashboardDto> GetVolunteerDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Volunteer);
        var profile = await _repository.GetVolunteerProfileByUserIdAsync(userId, cancellationToken);
        if (profile is null)
        {
            return EmptyVolunteerDashboard(
                null,
                "Your volunteer profile has not been created. Contact an administrator before handling requests.");
        }

        if (!profile.IsApproved)
        {
            var message = profile.IsActive
                ? "Your volunteer profile is awaiting administrator approval."
                : "Your volunteer application is not approved. Contact an administrator if you need help.";
            return EmptyVolunteerDashboard(MapVolunteer(profile), message);
        }

        if (!profile.IsActive || profile.User?.IsActive == false)
            return EmptyVolunteerDashboard(MapVolunteer(profile), "Your volunteer support access is inactive.");

        var requests = await _repository.GetRequestsForVolunteerAsync(userId, cancellationToken);
        var mapped = requests.Select(MapRequest).ToList();

        return new VolunteerDashboardDto(
            MapVolunteer(profile),
            CanHandleRequests: true,
            AccessMessage: null,
            PendingRequests: mapped
                .Where(request => request.Status == SupportRequestStatus.Pending)
                .OrderBy(request => request.CreatedAtUtc)
                .ToList()
                .AsReadOnly(),
            ActiveRequests: mapped
                .Where(request => request.Status == SupportRequestStatus.Accepted)
                .OrderBy(request => request.CreatedAtUtc)
                .ToList()
                .AsReadOnly(),
            History: mapped
                .Where(request => request.Status is SupportRequestStatus.Completed or SupportRequestStatus.Escalated)
                .OrderByDescending(request => request.CompletedAtUtc ?? request.EscalatedAtUtc ?? request.CreatedAtUtc)
                .ToList()
                .AsReadOnly());
    }

    public async Task<SupportRequestDto?> GetRequestForVolunteerAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Volunteer);
        if (requestId <= 0) return null;

        var request = await _repository.GetRequestForVolunteerAsync(requestId, userId, cancellationToken);
        return request is null ? null : MapRequest(request);
    }

    public async Task<bool> AcceptRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var (userId, volunteer) = await RequireActiveVolunteerAsync(cancellationToken);
        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        if (request is null ||
            request.Status != SupportRequestStatus.Pending ||
            !request.IsVisibleToVolunteers ||
            request.VolunteerProfileId.HasValue)
            return false;

        if (!await _repository.HasActiveAssignmentAsync(volunteer.Id, request.StudentProfileId, cancellationToken) ||
            await _repository.HasVolunteerDeclinedRequestAsync(request.Id, userId, cancellationToken))
            return false;

        request.VolunteerProfileId = volunteer.Id;
        request.VolunteerProfile = volunteer;
        request.Status = SupportRequestStatus.Accepted;
        request.IsVisibleToVolunteers = false;
        request.ModifiedAtUtc = DateTime.UtcNow;
        _auditLog.Record(
            "SupportRequest.Accept",
            nameof(SupportRequest),
            request.Id.ToString(),
            "Assigned volunteer accepted a peer-support request.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var (userId, volunteer) = await RequireActiveVolunteerAsync(cancellationToken);
        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        if (request is null ||
            request.Status != SupportRequestStatus.Pending ||
            !request.IsVisibleToVolunteers ||
            request.VolunteerProfileId.HasValue)
            return false;

        if (!await _repository.HasActiveAssignmentAsync(volunteer.Id, request.StudentProfileId, cancellationToken) ||
            await _repository.HasVolunteerDeclinedRequestAsync(request.Id, userId, cancellationToken))
            return false;

        await _repository.AddInteractionAsync(new SupportInteraction
        {
            SupportRequestId = request.Id,
            VolunteerUserId = userId,
            StudentUserId = request.StudentProfile?.UserId,
            Type = SupportInteractionType.VolunteerDeclined,
            Message = "Volunteer declined this request.",
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        _auditLog.Record(
            "SupportRequest.Decline",
            nameof(SupportRequest),
            request.Id.ToString(),
            "Assigned volunteer declined a pending peer-support request; it remains pending for other assigned volunteers.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SupportInteractionDto?> AddInteractionAsync(
        int requestId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var (userId, volunteer) = await RequireActiveVolunteerAsync(cancellationToken);
        var normalizedMessage = NormalizeRequired(message, MaximumMessageLength, nameof(message));
        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        if (!IsOwnedActiveRequest(request, volunteer.Id)) return null;

        var interaction = new SupportInteraction
        {
            SupportRequestId = request!.Id,
            VolunteerUserId = userId,
            StudentUserId = request.StudentProfile?.UserId,
            Type = SupportInteractionType.Message,
            Message = normalizedMessage,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddInteractionAsync(interaction, cancellationToken);
        request.ModifiedAtUtc = interaction.CreatedAtUtc;
        _auditLog.Record(
            "SupportInteraction.Create",
            nameof(SupportInteraction),
            details: $"Volunteer added a peer-guidance interaction to request {request.Id}.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapInteraction(interaction);
    }

    public async Task<bool> CompleteRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var (userId, volunteer) = await RequireActiveVolunteerAsync(cancellationToken);
        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        if (!IsOwnedActiveRequest(request, volunteer.Id)) return false;

        var nowUtc = DateTime.UtcNow;
        request!.Status = SupportRequestStatus.Completed;
        request.CompletedAtUtc = nowUtc;
        request.ModifiedAtUtc = nowUtc;
        await _repository.AddInteractionAsync(new SupportInteraction
        {
            SupportRequestId = request.Id,
            VolunteerUserId = userId,
            StudentUserId = request.StudentProfile?.UserId,
            Type = SupportInteractionType.Completed,
            Message = "Peer-support request marked complete.",
            IsCompleted = true,
            CreatedAtUtc = nowUtc
        }, cancellationToken);
        _auditLog.Record(
            "SupportRequest.Complete",
            nameof(SupportRequest),
            request.Id.ToString(),
            "Assigned volunteer marked the peer-support request complete.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> EscalateRequestAsync(
        int requestId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var (userId, volunteer) = await RequireActiveVolunteerAsync(cancellationToken);
        var request = await _repository.GetRequestByIdAsync(requestId, cancellationToken);
        if (!IsOwnedActiveRequest(request, volunteer.Id)) return false;

        var escalationMessage = NormalizeOptional(message, MaximumMessageLength, nameof(message))
            ?? "A peer volunteer requested counselor follow-up.";
        var nowUtc = DateTime.UtcNow;
        request!.Status = SupportRequestStatus.Escalated;
        request.EscalatedAtUtc = nowUtc;
        request.ModifiedAtUtc = nowUtc;
        await _repository.AddInteractionAsync(new SupportInteraction
        {
            SupportRequestId = request.Id,
            VolunteerUserId = userId,
            StudentUserId = request.StudentProfile?.UserId,
            Type = SupportInteractionType.Escalated,
            Message = escalationMessage,
            EscalatedToCounselor = true,
            CreatedAtUtc = nowUtc
        }, cancellationToken);
        _auditLog.Record(
            "SupportRequest.Escalate",
            nameof(SupportRequest),
            request.Id.ToString(),
            "Assigned volunteer escalated a peer-support request for counselor follow-up.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyCounselorsOfEscalationAsync(request.Id, cancellationToken);
        return true;
    }

    private async Task<bool> ReviewVolunteerAsync(
        int volunteerProfileId,
        bool approve,
        CancellationToken cancellationToken)
    {
        RequireUser(RoleConstants.Admin);
        ArgumentOutOfRangeException.ThrowIfLessThan(volunteerProfileId, 1);

        var volunteer = await _repository.GetVolunteerProfileByIdAsync(volunteerProfileId, cancellationToken);
        if (volunteer is null) return false;

        volunteer.IsApproved = approve;
        volunteer.IsActive = approve;
        volunteer.ModifiedAtUtc = DateTime.UtcNow;
        if (!approve)
        {
            foreach (var assignment in volunteer.VolunteerAssignments.Where(assignment => assignment.IsActive))
            {
                assignment.IsActive = false;
                assignment.ModifiedAtUtc = volunteer.ModifiedAtUtc;
            }
        }

        _auditLog.Record(
            approve ? "VolunteerProfile.Approve" : "VolunteerProfile.Reject",
            nameof(VolunteerProfile),
            volunteer.Id.ToString(),
            approve ? "Volunteer application approved." : "Volunteer application rejected and assignments deactivated.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(string UserId, VolunteerProfile Volunteer)> RequireActiveVolunteerAsync(
        CancellationToken cancellationToken)
    {
        var userId = RequireUser(RoleConstants.Volunteer);
        var volunteer = await _repository.GetVolunteerProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Volunteer profile not found.");
        if (!volunteer.IsApproved || !volunteer.IsActive || volunteer.User?.IsActive == false)
            throw new UnauthorizedAccessException("Volunteer support access is inactive or awaiting approval.");
        return (userId, volunteer);
    }

    private async Task NotifyCounselorsOfEscalationAsync(int requestId, CancellationToken cancellationToken)
    {
        try
        {
            var counselorUserIds = await _repository.GetActiveCounselorUserIdsAsync(cancellationToken);
            if (counselorUserIds.Count == 0)
            {
                await _notificationService.NotifyAdministratorsAsync(
                    NotificationType.PeerSupportEscalation,
                    "Peer-support escalation needs routing",
                    $"Peer-support request #{requestId} was escalated, but no active counselor account is available.",
                    cancellationToken);
                return;
            }

            foreach (var counselorUserId in counselorUserIds)
            {
                await _notificationService.CreateAsync(
                    counselorUserId,
                    NotificationType.PeerSupportEscalation,
                    "Peer-support request escalated",
                    $"Peer-support request #{requestId} requires counselor follow-up. No risk score or private journal data is included.",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Peer-support request {RequestId} was escalated, but counselor notifications could not be delivered.", requestId);
        }
    }

    private string RequireUser(string role)
    {
        if (!_currentUser.IsAuthenticated ||
            string.IsNullOrWhiteSpace(_currentUser.UserId) ||
            !_currentUser.IsInRole(role))
            throw new UnauthorizedAccessException("The current user is not authorized for this volunteer-support operation.");

        return _currentUser.UserId;
    }

    private static bool IsOwnedActiveRequest(SupportRequest? request, int volunteerProfileId)
        => request is not null &&
           request.VolunteerProfileId == volunteerProfileId &&
           request.Status == SupportRequestStatus.Accepted;

    private static VolunteerDashboardDto EmptyVolunteerDashboard(
        VolunteerProfileDto? profile,
        string message)
        => new(
            profile,
            CanHandleRequests: false,
            AccessMessage: message,
            PendingRequests: Array.Empty<SupportRequestDto>(),
            ActiveRequests: Array.Empty<SupportRequestDto>(),
            History: Array.Empty<SupportRequestDto>());

    private static VolunteerProfileDto MapVolunteer(VolunteerProfile volunteer)
        => new(
            volunteer.Id,
            volunteer.UserId,
            FirstNonEmpty(volunteer.User?.FullName, volunteer.User?.Email, "Volunteer"),
            volunteer.Department,
            volunteer.Skills,
            volunteer.Availability,
            volunteer.IsApproved,
            volunteer.IsActive,
            volunteer.Bio);

    private static AdminVolunteerDto MapAdminVolunteer(VolunteerProfile volunteer)
        => new(
            volunteer.Id,
            FirstNonEmpty(volunteer.User?.FullName, volunteer.User?.Email, "Volunteer"),
            volunteer.Department,
            volunteer.Skills,
            volunteer.Availability,
            GetApprovalState(volunteer),
            volunteer.VolunteerAssignments.Count(assignment => assignment.IsActive),
            GetPendingRequestsForVolunteer(volunteer).Select(request => request.Id).Distinct().Count());

    private static IEnumerable<SupportRequest> GetPendingRequestsForVolunteer(VolunteerProfile volunteer)
        => volunteer.VolunteerAssignments
            .Where(assignment => assignment.IsActive)
            .SelectMany(assignment => assignment.StudentProfile?.SupportRequests ?? Array.Empty<SupportRequest>())
            .Where(request => request.Status == SupportRequestStatus.Pending &&
                              request.IsVisibleToVolunteers &&
                              request.VolunteerProfileId == null &&
                              !request.Interactions.Any(interaction =>
                                  interaction.VolunteerUserId == volunteer.UserId &&
                                  interaction.Type == SupportInteractionType.VolunteerDeclined));

    private static VolunteerApprovalState GetApprovalState(VolunteerProfile volunteer)
        => (volunteer.IsApproved, volunteer.IsActive) switch
        {
            (true, true) => VolunteerApprovalState.Approved,
            (true, false) => VolunteerApprovalState.Inactive,
            (false, true) => VolunteerApprovalState.Pending,
            _ => VolunteerApprovalState.Rejected
        };

    private static VolunteerAssignmentDto MapAssignment(VolunteerAssignment assignment)
        => new(
            assignment.Id,
            assignment.StudentProfileId,
            StudentDisplayName(assignment.StudentProfile),
            assignment.StudentProfile?.Program,
            assignment.StudentProfile?.EnrollmentYear ?? 0,
            assignment.Role,
            assignment.GroupName,
            assignment.Notes,
            assignment.CreatedAtUtc);

    private static SupportRequestDto MapRequest(SupportRequest request)
    {
        var interactions = request.Interactions
            .Where(interaction => interaction.Type != SupportInteractionType.VolunteerDeclined)
            .OrderBy(interaction => interaction.CreatedAtUtc)
            .Select(MapInteraction)
            .ToList()
            .AsReadOnly();

        return new SupportRequestDto(
            request.Id,
            request.Category,
            request.Title,
            request.Message,
            request.Availability,
            request.Status,
            StudentDisplayName(request.StudentProfile),
            request.VolunteerProfile is null
                ? null
                : FirstNonEmpty(request.VolunteerProfile.User?.FullName, request.VolunteerProfile.User?.Email, "Volunteer"),
            request.CreatedAtUtc,
            request.CompletedAtUtc,
            request.EscalatedAtUtc,
            interactions);
    }

    private static SupportInteractionDto MapInteraction(SupportInteraction interaction)
        => new(
            interaction.Id,
            interaction.Type,
            interaction.Message,
            IsFromVolunteer: !string.IsNullOrWhiteSpace(interaction.VolunteerUserId),
            interaction.IsCompleted,
            interaction.EscalatedToCounselor,
            interaction.CreatedAtUtc);

    private static string StudentDisplayName(StudentProfile? student)
        => FirstNonEmpty(student?.User?.FullName, student?.User?.Email, "Assigned student");

    private static string GroupDisplayName(string program, int enrollmentYear)
    {
        var studyYear = DateTime.UtcNow.Year - enrollmentYear + 1;
        var yearLabel = studyYear switch
        {
            1 => "First year",
            2 => "Second year",
            3 => "Third year",
            4 => "Fourth year",
            5 => "Fifth year",
            _ => $"{enrollmentYear} cohort"
        };
        return $"{yearLabel} {program.Trim()} students";
    }

    private static string CategoryLabel(SupportRequestCategory category)
        => category switch
        {
            SupportRequestCategory.AcademicGuidance => "Academic guidance",
            SupportRequestCategory.CampusAdjustment => "Campus adjustment",
            SupportRequestCategory.PeerDiscussion => "Peer discussion",
            SupportRequestCategory.TechnicalHelp => "Technical help",
            SupportRequestCategory.GeneralSupport => "General support",
            _ => "Peer support"
        };

    private static string NormalizeRequired(string? value, int maximumLength, string parameterName)
        => NormalizeOptional(value, maximumLength, parameterName)
            ?? throw new ArgumentException("A value is required.", parameterName);

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
