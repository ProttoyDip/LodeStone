using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Repositories;

public sealed class VolunteerSupportRepository : GenericRepository<SupportRequest>, IVolunteerSupportRepository
{
    public VolunteerSupportRepository(ApplicationDbContext context) : base(context) { }

    public Task<StudentProfile?> GetStudentProfileByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => Context.StudentProfiles
            .Include(profile => profile.User)
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<StudentProfile>> GetStudentsAsync(
        CancellationToken cancellationToken = default)
        => await Context.StudentProfiles
            .Include(profile => profile.User)
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.UserId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StudentProfile>> GetStudentsByGroupAsync(
        string program,
        int enrollmentYear,
        CancellationToken cancellationToken = default)
        => await Context.StudentProfiles
            .Include(profile => profile.User)
            .Where(profile => profile.Program == program && profile.EnrollmentYear == enrollmentYear)
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.UserId)
            .ToListAsync(cancellationToken);

    public Task<VolunteerProfile?> GetVolunteerProfileByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => Context.VolunteerProfiles
            .Include(profile => profile.User)
            .FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public Task<VolunteerProfile?> GetVolunteerProfileByIdAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
        => Context.VolunteerProfiles
            .Include(profile => profile.User)
            .Include(profile => profile.VolunteerAssignments)
            .FirstOrDefaultAsync(profile => profile.Id == volunteerProfileId, cancellationToken);

    public Task CreateVolunteerProfileAsync(
        VolunteerProfile volunteer,
        CancellationToken cancellationToken = default)
        => Context.VolunteerProfiles.AddAsync(volunteer, cancellationToken).AsTask();

    public Task<ApplicationUser?> GetTrackedUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => Context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<VolunteerProfile>> GetAvailableVolunteersAsync(
        CancellationToken cancellationToken = default)
        => await Context.VolunteerProfiles
            .Include(profile => profile.User)
            .Where(profile => profile.IsApproved &&
                              profile.IsActive &&
                              profile.User != null &&
                              profile.User.IsActive)
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.UserId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<VolunteerProfile>> GetVolunteersForAdminAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var volunteers = Context.VolunteerProfiles
            .Include(profile => profile.User)
            .Include(profile => profile.VolunteerAssignments.Where(assignment => assignment.IsActive))
                .ThenInclude(assignment => assignment.StudentProfile)
                    .ThenInclude(student => student!.SupportRequests.Where(request =>
                        request.Status == SupportRequestStatus.Pending && request.IsVisibleToVolunteers))
                        .ThenInclude(request => request.Interactions)
            .AsSplitQuery()
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim();
            volunteers = volunteers.Where(profile =>
                (profile.User != null &&
                 (profile.User.FullName.Contains(normalized) ||
                  (profile.User.Email != null && profile.User.Email.Contains(normalized)))) ||
                (profile.Department != null && profile.Department.Contains(normalized)) ||
                (profile.Skills != null && profile.Skills.Contains(normalized)));
        }

        return await volunteers
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.UserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VolunteerAssignment>> GetAssignmentsForVolunteerAsync(
        int volunteerProfileId,
        CancellationToken cancellationToken = default)
        => await Context.VolunteerAssignments
            .Include(assignment => assignment.StudentProfile)
                .ThenInclude(profile => profile!.User)
            .Where(assignment => assignment.VolunteerProfileId == volunteerProfileId)
            .OrderByDescending(assignment => assignment.IsActive)
            .ThenBy(assignment => assignment.StudentProfile != null && assignment.StudentProfile.User != null
                ? assignment.StudentProfile.User.FullName
                : assignment.StudentProfileId.ToString())
            .ToListAsync(cancellationToken);

    public Task<VolunteerAssignment?> GetAssignmentByIdAsync(
        int assignmentId,
        CancellationToken cancellationToken = default)
        => Context.VolunteerAssignments
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);

    public Task AddVolunteerAssignmentsAsync(
        IEnumerable<VolunteerAssignment> assignments,
        CancellationToken cancellationToken = default)
        => Context.VolunteerAssignments.AddRangeAsync(assignments, cancellationToken);

    public Task<SupportRequest?> GetRequestByIdAsync(
        int requestId,
        CancellationToken cancellationToken = default)
        => RequestQuery(tracking: true)
            .FirstOrDefaultAsync(request => request.Id == requestId, cancellationToken);

    public Task<SupportRequest?> GetRequestForStudentAsync(
        int requestId,
        string studentUserId,
        CancellationToken cancellationToken = default)
        => RequestQuery(tracking: false)
            .FirstOrDefaultAsync(
                request => request.Id == requestId &&
                           request.StudentProfile != null &&
                           request.StudentProfile.UserId == studentUserId,
                cancellationToken);

    public async Task<SupportRequest?> GetRequestForVolunteerAsync(
        int requestId,
        string volunteerUserId,
        CancellationToken cancellationToken = default)
    {
        var volunteerId = await ActiveVolunteerIds(volunteerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (volunteerId == 0) return null;

        return await RequestQuery(tracking: false)
            .FirstOrDefaultAsync(
                request => request.Id == requestId &&
                    (request.VolunteerProfileId == volunteerId ||
                     (request.Status == SupportRequestStatus.Pending &&
                      request.IsVisibleToVolunteers &&
                      request.VolunteerProfileId == null &&
                      request.StudentProfile != null &&
                      request.StudentProfile.VolunteerAssignments.Any(assignment =>
                          assignment.VolunteerProfileId == volunteerId && assignment.IsActive) &&
                      !request.Interactions.Any(interaction =>
                          interaction.VolunteerUserId == volunteerUserId &&
                          interaction.Type == SupportInteractionType.VolunteerDeclined))),
                cancellationToken);
    }

    public async Task<IReadOnlyList<SupportRequest>> GetRequestsForVolunteerAsync(
        string volunteerUserId,
        CancellationToken cancellationToken = default)
    {
        var volunteerId = await ActiveVolunteerIds(volunteerUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (volunteerId == 0) return Array.Empty<SupportRequest>();

        return await RequestQuery(tracking: false)
            .Where(request =>
                request.VolunteerProfileId == volunteerId ||
                (request.Status == SupportRequestStatus.Pending &&
                 request.IsVisibleToVolunteers &&
                 request.VolunteerProfileId == null &&
                 request.StudentProfile != null &&
                 request.StudentProfile.VolunteerAssignments.Any(assignment =>
                     assignment.VolunteerProfileId == volunteerId && assignment.IsActive) &&
                 !request.Interactions.Any(interaction =>
                     interaction.VolunteerUserId == volunteerUserId &&
                     interaction.Type == SupportInteractionType.VolunteerDeclined)))
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveAssignmentAsync(
        int volunteerProfileId,
        int studentProfileId,
        CancellationToken cancellationToken = default)
        => Context.VolunteerAssignments.AnyAsync(
            assignment => assignment.VolunteerProfileId == volunteerProfileId &&
                          assignment.StudentProfileId == studentProfileId &&
                          assignment.IsActive,
            cancellationToken);

    public Task<bool> HasVolunteerDeclinedRequestAsync(
        int requestId,
        string volunteerUserId,
        CancellationToken cancellationToken = default)
        => Context.SupportInteractions.AnyAsync(
            interaction => interaction.SupportRequestId == requestId &&
                           interaction.VolunteerUserId == volunteerUserId &&
                           interaction.Type == SupportInteractionType.VolunteerDeclined,
            cancellationToken);

    public Task AddSupportRequestAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default)
        => Context.SupportRequests.AddAsync(request, cancellationToken).AsTask();

    public Task AddInteractionAsync(
        SupportInteraction interaction,
        CancellationToken cancellationToken = default)
        => Context.SupportInteractions.AddAsync(interaction, cancellationToken).AsTask();

    public async Task<IReadOnlyList<SupportRequest>> GetRequestsForStudentAsync(
        string studentUserId,
        CancellationToken cancellationToken = default)
        => await RequestQuery(tracking: false)
            .Where(request => request.StudentProfile != null &&
                              request.StudentProfile.UserId == studentUserId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveCounselorUserIdsAsync(
        CancellationToken cancellationToken = default)
        => await Context.CounselorProfiles
            .Where(profile => profile.User != null && profile.User.IsActive)
            .Select(profile => profile.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private IQueryable<int> ActiveVolunteerIds(string volunteerUserId)
        => Context.VolunteerProfiles
            .Where(profile => profile.UserId == volunteerUserId &&
                              profile.IsApproved &&
                              profile.IsActive &&
                              profile.User != null &&
                              profile.User.IsActive)
            .Select(profile => profile.Id);

    private IQueryable<SupportRequest> RequestQuery(bool tracking)
    {
        var query = Context.SupportRequests
            .Include(request => request.StudentProfile)
                .ThenInclude(profile => profile!.User)
            .Include(request => request.VolunteerProfile)
                .ThenInclude(profile => profile!.User)
            .Include(request => request.Interactions)
            .AsSplitQuery();

        return tracking ? query : query.AsNoTracking();
    }
}
