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
    private readonly IPeerSupportNotifier _peerSupportNotifier;
    private readonly IVolunteerRosterNotifier _volunteerRosterNotifier;
    private readonly ILogger<VolunteerSupportService> _logger;

    public VolunteerSupportService(
        IVolunteerSupportRepository repository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLog,
        INotificationService notificationService,
        IPeerSupportNotifier peerSupportNotifier,
        IVolunteerRosterNotifier volunteerRosterNotifier,
        ILogger<VolunteerSupportService> logger)
    {
        _repository = repository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _auditLog = auditLog;
        _notificationService = notificationService;
        _peerSupportNotifier = peerSupportNotifier;
        _volunteerRosterNotifier = volunteerRosterNotifier;
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

        var fullName = (dto.FullName ?? string.Empty).Trim();
        if (fullName.Length is < 2 or > 150)
            throw new ArgumentException("Enter your full name.", nameof(dto.FullName));

        // An invitation carries only an email address, so the volunteer supplies the display name
        // the rest of the application shows for them.
        var account = await _repository.GetTrackedUserAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("The signed-in account could not be found.");
        account.FullName = fullName;

        var volunteer = new VolunteerProfile
        {
            UserId = userId,
            User = account,
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

        // The profile is the thing an administrator reviews, so tell them it is waiting. The
        // volunteer's own details stay out of the notification body.
        await _notificationService.NotifyAdministratorsAsync(
            NotificationType.System,
            "Volunteer profile awaiting approval",
            "An invited volunteer completed their profile and is waiting for approval.",
            cancellationToken);
        await NotifyRosterChangedAsync(cancellationToken);

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

    /// <summary>How many suggestions to show per request. Enough to choose between, few enough to read.</summary>
    public const int SuggestionsPerRequest = 3;

    public async Task<SupportRequestRoutingDto> GetRequestRoutingAsync(
        CancellationToken cancellationToken = default)
    {
        RequireUser(RoleConstants.Admin);

        var requests = await _repository.GetUnroutedPendingRequestsAsync(cancellationToken);
        var volunteers = await _repository.GetAvailableVolunteersAsync(cancellationToken);
        if (requests.Count == 0)
            return new SupportRequestRoutingDto(Array.Empty<SupportRequestRoutingItemDto>(), volunteers.Count);

        var workload = await _repository.GetActiveAssignmentCountsAsync(cancellationToken);
        var candidates = volunteers
            .Select(volunteer => new VolunteerMatchCandidate(
                volunteer.Id,
                FirstNonEmpty(volunteer.User?.FullName, volunteer.User?.Email, "Volunteer"),
                volunteer.Skills,
                volunteer.Department,
                volunteer.Bio,
                volunteer.Availability,
                workload.TryGetValue(volunteer.Id, out var open) ? open : 0))
            .ToArray();

        var items = requests
            .Select(request => new SupportRequestRoutingItemDto(
                request.Id,
                request.StudentProfileId,
                StudentDisplayName(request.StudentProfile),
                request.Category,
                request.Title,
                request.Availability,
                request.CreatedAtUtc,
                VolunteerMatcher.Rank(
                        new VolunteerMatchRequest(request.Category, request.Message, request.Availability),
                        candidates)
                    .Take(SuggestionsPerRequest)
                    .ToArray()))
            .ToList()
            .AsReadOnly();

        return new SupportRequestRoutingDto(items, volunteers.Count);
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
        await NotifyRosterChangedAsync(cancellationToken);
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

        await NotifyParticipantsAsync(request, cancellationToken);
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

    public async Task<IReadOnlyList<AssignedVolunteerDto>> GetAssignedVolunteersForStudentAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Student);
        var student = await _repository.GetStudentProfileByUserIdAsync(userId, cancellationToken);
        if (student is null) return Array.Empty<AssignedVolunteerDto>();

        var assignments = await _repository.GetActiveAssignmentsForStudentAsync(student.Id, cancellationToken);
        if (assignments.Count == 0) return Array.Empty<AssignedVolunteerDto>();

        // One open conversation per volunteer is enough; surface it so the student resumes it.
        var openRequests = (await _repository.GetRequestsForStudentAsync(userId, cancellationToken))
            .Where(request => request.Status == SupportRequestStatus.Accepted && request.VolunteerProfileId.HasValue)
            .GroupBy(request => request.VolunteerProfileId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(request => request.CreatedAtUtc).First().Id);

        return assignments
            .Where(assignment => assignment.VolunteerProfile is not null)
            .GroupBy(assignment => assignment.VolunteerProfileId)
            .Select(group =>
            {
                var assignment = group.OrderByDescending(item => item.CreatedAtUtc).First();
                var volunteer = assignment.VolunteerProfile!;
                return new AssignedVolunteerDto(
                    volunteer.Id,
                    FirstNonEmpty(volunteer.User?.FullName, volunteer.User?.Email, "Volunteer"),
                    string.IsNullOrWhiteSpace(assignment.Role) ? "Peer volunteer" : assignment.Role,
                    volunteer.Department,
                    volunteer.Skills,
                    volunteer.Availability,
                    volunteer.Bio,
                    openRequests.TryGetValue(volunteer.Id, out var requestId) ? requestId : null);
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task<int> StartConversationWithVolunteerAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser(RoleConstants.Student);
        ArgumentOutOfRangeException.ThrowIfLessThan(volunteerProfileId, 1);

        var student = await _repository.GetStudentProfileByUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Student profile not found.");

        var assignment = (await _repository.GetActiveAssignmentsForStudentAsync(student.Id, cancellationToken))
            .FirstOrDefault(item => item.VolunteerProfileId == volunteerProfileId)
            ?? throw new InvalidOperationException("That volunteer is not currently assigned to you.");

        var existing = (await _repository.GetRequestsForStudentAsync(userId, cancellationToken))
            .Where(request => request.Status == SupportRequestStatus.Accepted && request.VolunteerProfileId == volunteerProfileId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .FirstOrDefault();
        if (existing is not null) return existing.Id;

        var nowUtc = DateTime.UtcNow;
        // Assignments are loaded untracked; attaching the navigation would make EF insert the volunteer graph again.
        var request = new SupportRequest
        {
            StudentProfileId = student.Id,
            StudentProfile = student,
            VolunteerProfileId = volunteerProfileId,
            Category = SupportRequestCategory.PeerDiscussion,
            Title = "Conversation with your volunteer",
            Message = "Started directly from the student dashboard with an assigned volunteer.",
            Status = SupportRequestStatus.Accepted,
            IsVisibleToVolunteers = false,
            CreatedAtUtc = nowUtc
        };

        await _repository.AddSupportRequestAsync(request, cancellationToken);
        _auditLog.Record(
            "SupportRequest.StartConversation",
            nameof(SupportRequest),
            details: "Student opened a direct conversation with an assigned volunteer.");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyParticipantsAsync(request, cancellationToken);
        if (!string.IsNullOrWhiteSpace(assignment.VolunteerProfile?.UserId))
        {
            try
            {
                await _notificationService.CreateAsync(
                    assignment.VolunteerProfile.UserId,
                    NotificationType.System,
                    "A student opened a conversation",
                    "A student you are assigned to started a private peer-support conversation with you. Open your volunteer dashboard to reply.",
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not notify the volunteer about a new direct conversation.");
            }
        }
        return request.Id;
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
                "Complete your volunteer profile so an administrator can approve you for peer support.");
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
        await NotifyParticipantsAsync(request, cancellationToken);
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
        await NotifyParticipantsAsync(request, cancellationToken);
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
        await NotifyParticipantsAsync(request, cancellationToken);
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
        await NotifyParticipantsAsync(request, cancellationToken);
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
        await NotifyParticipantsAsync(request, cancellationToken);
        return true;
    }

    /// <summary>
    /// Tells the people who can currently see this request that it changed, so their page refreshes
    /// itself. The set is resolved server-side and always includes the student, whichever volunteer
    /// holds it, and every volunteer assigned to that student -- the last of those matters because a
    /// request being accepted or completed removes it from other volunteers' lists too.
    /// </summary>
    private async Task NotifyParticipantsAsync(SupportRequest request, CancellationToken cancellationToken)
    {
        // Everything here runs after the change is committed, so resolving the audience is as
        // non-fatal as delivering to it: neither may turn a saved change into an error.
        try
        {
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(request.StudentProfile?.UserId))
                recipients.Add(request.StudentProfile!.UserId);
            if (!string.IsNullOrWhiteSpace(request.VolunteerProfile?.UserId))
                recipients.Add(request.VolunteerProfile!.UserId);

            var assigned = await _repository.GetAssignedVolunteerUserIdsAsync(
                request.StudentProfileId,
                cancellationToken);
            foreach (var volunteerUserId in assigned ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(volunteerUserId)) recipients.Add(volunteerUserId);
            }

            if (recipients.Count == 0) return;

            await _peerSupportNotifier.NotifyChangedAsync(recipients.ToArray(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The change is already committed. A transport failure must not undo it; the page
            // still shows the new state on its next load.
            _logger.LogWarning(
                exception,
                "Could not signal peer-support participants for request {RequestId}.",
                request.Id);
        }
    }

    /// <summary>
    /// Tells administrators their volunteer list is out of date. Best-effort by design: this runs
    /// after the roster change is committed, so a transport failure must not turn a saved change
    /// into an error for whoever made it.
    /// </summary>
    private async Task NotifyRosterChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _volunteerRosterNotifier.NotifyRosterChangedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not signal administrators that the volunteer roster changed.");
        }
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
        await NotifyRosterChangedAsync(cancellationToken);
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
