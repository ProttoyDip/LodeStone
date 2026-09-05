using FluentAssertions;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Lodestone.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lodestone.IntegrationTests.Services;

/// <summary>
/// Removing staff must never destroy records that describe students. These tests cover what
/// happens to appointments, session reports, support requests and assignments.
/// </summary>
public sealed class StaffRemovalTests
{
    // ---------- counselors ----------

    [Fact]
    public async Task Counselor_with_no_appointments_is_deleted_outright()
    {
        await using var context = CreateContext();
        var counselor = await SeedCounselorAsync(context, "leaver@university.test");
        var users = UserManagerFor(counselor.User!);
        var service = new CounselorProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(counselor.Id, null);

        result.Succeeded.Should().BeTrue();
        result.TransferredItems.Should().Be(0);
        (await context.CounselorProfiles.CountAsync()).Should().Be(0);
        users.Verify(m => m.DeleteAsync(counselor.User!), Times.Once);
    }

    [Fact]
    public async Task Counselor_with_appointments_is_refused_without_a_replacement()
    {
        await using var context = CreateContext();
        var counselor = await SeedCounselorAsync(context, "leaver@university.test");
        var student = await SeedStudentAsync(context);
        await SeedBookingAsync(context, counselor, student);
        var users = UserManagerFor(counselor.User!);
        var service = new CounselorProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(counselor.Id, null);

        result.Succeeded.Should().BeFalse();
        result.RequiresReplacement.Should().BeTrue();
        result.TransferredItems.Should().Be(1);

        // Nothing is touched until somewhere is chosen for the appointments to go.
        (await context.CounselorProfiles.CountAsync()).Should().Be(1);
        (await context.CounselorBookings.CountAsync()).Should().Be(1);
        users.Verify(m => m.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Counselor_appointments_and_session_reports_move_to_the_replacement()
    {
        await using var context = CreateContext();
        var leaver = await SeedCounselorAsync(context, "leaver@university.test");
        var successor = await SeedCounselorAsync(context, "successor@university.test");
        var student = await SeedStudentAsync(context);
        var booking = await SeedBookingAsync(context, leaver, student);
        context.CounselorSessionReports.Add(new CounselorSessionReport
        {
            CounselorBookingId = booking.Id,
            Summary = "Session notes about the student.",
            Status = ReportStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var users = UserManagerFor(leaver.User!);
        var service = new CounselorProvisioningService(users.Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(leaver.Id, successor.Id);

        result.Succeeded.Should().BeTrue();
        result.TransferredItems.Should().Be(1);

        var moved = await context.CounselorBookings.SingleAsync();
        moved.CounselorProfileId.Should().Be(successor.Id);
        // The report hangs off the booking, so it follows without being touched.
        (await context.CounselorSessionReports.CountAsync()).Should().Be(1);
        (await context.CounselorProfiles.SingleAsync()).Id.Should().Be(successor.Id);
    }

    [Fact]
    public async Task Counselor_availability_slots_are_removed_without_orphaning_the_appointment()
    {
        await using var context = CreateContext();
        var leaver = await SeedCounselorAsync(context, "leaver@university.test");
        var successor = await SeedCounselorAsync(context, "successor@university.test");
        var student = await SeedStudentAsync(context);

        var slot = new CounselorAvailabilitySlot
        {
            CounselorProfileId = leaver.Id,
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            IsBooked = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.CounselorAvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        var booking = await SeedBookingAsync(context, leaver, student);
        booking.AvailabilitySlotId = slot.Id;
        await context.SaveChangesAsync();

        var service = new CounselorProvisioningService(
            UserManagerFor(leaver.User!).Object, context, Mock.Of<IAuditLogService>());

        await service.RemoveAsync(leaver.Id, successor.Id);

        (await context.CounselorAvailabilitySlots.CountAsync()).Should().Be(0);
        var moved = await context.CounselorBookings.SingleAsync();
        moved.AvailabilitySlotId.Should().BeNull("the slot belonged to the departing counselor");
        moved.ScheduledForUtc.Should().NotBe(default, "the appointment keeps its own time");
    }

    [Fact]
    public async Task Counselor_cannot_be_replaced_by_themselves()
    {
        await using var context = CreateContext();
        var counselor = await SeedCounselorAsync(context, "leaver@university.test");
        var student = await SeedStudentAsync(context);
        await SeedBookingAsync(context, counselor, student);
        var service = new CounselorProvisioningService(
            UserManagerFor(counselor.User!).Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(counselor.Id, counselor.Id);

        result.Succeeded.Should().BeFalse();
        (await context.CounselorProfiles.CountAsync()).Should().Be(1);
    }

    // ---------- volunteers ----------

    [Fact]
    public async Task Volunteer_support_requests_move_to_the_replacement()
    {
        await using var context = CreateContext();
        var leaver = await SeedVolunteerAsync(context, "leaver@university.test");
        var successor = await SeedVolunteerAsync(context, "successor@university.test");
        var student = await SeedStudentAsync(context);
        context.SupportRequests.Add(new SupportRequest
        {
            StudentProfileId = student.Id,
            VolunteerProfileId = leaver.Id,
            Category = SupportRequestCategory.AcademicGuidance,
            Title = "Academic guidance",
            Message = "A student's own words.",
            Status = SupportRequestStatus.Accepted,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = new VolunteerProvisioningService(
            UserManagerFor(leaver.User!).Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(leaver.Id, successor.Id);

        result.Succeeded.Should().BeTrue();
        result.TransferredItems.Should().Be(1);
        var moved = await context.SupportRequests.SingleAsync();
        moved.VolunteerProfileId.Should().Be(successor.Id);
        moved.Message.Should().Be("A student's own words.");
    }

    [Fact]
    public async Task Volunteer_with_support_requests_is_refused_without_a_replacement()
    {
        await using var context = CreateContext();
        var leaver = await SeedVolunteerAsync(context, "leaver@university.test");
        var student = await SeedStudentAsync(context);
        context.SupportRequests.Add(new SupportRequest
        {
            StudentProfileId = student.Id,
            VolunteerProfileId = leaver.Id,
            Category = SupportRequestCategory.GeneralSupport,
            Title = "General support",
            Message = "Please help.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = new VolunteerProvisioningService(
            UserManagerFor(leaver.User!).Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(leaver.Id, null);

        result.RequiresReplacement.Should().BeTrue();
        (await context.SupportRequests.CountAsync()).Should().Be(1);
        (await context.VolunteerProfiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Volunteer_assignment_is_dropped_when_the_replacement_already_mentors_that_student()
    {
        await using var context = CreateContext();
        var leaver = await SeedVolunteerAsync(context, "leaver@university.test");
        var successor = await SeedVolunteerAsync(context, "successor@university.test");
        var shared = await SeedStudentAsync(context);
        var onlyLeavers = await SeedStudentAsync(context);

        context.VolunteerAssignments.AddRange(
            Assignment(leaver.Id, shared.Id),
            Assignment(leaver.Id, onlyLeavers.Id),
            Assignment(successor.Id, shared.Id));
        context.SupportRequests.Add(new SupportRequest
        {
            StudentProfileId = shared.Id,
            VolunteerProfileId = leaver.Id,
            Category = SupportRequestCategory.PeerDiscussion,
            Title = "Peer discussion",
            Message = "Hello.",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = new VolunteerProvisioningService(
            UserManagerFor(leaver.User!).Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(leaver.Id, successor.Id);

        result.Succeeded.Should().BeTrue();
        // (VolunteerProfileId, StudentProfileId) is unique, so the duplicate is dropped rather
        // than moved, and the successor ends up mentoring each student exactly once.
        var assignments = await context.VolunteerAssignments.ToListAsync();
        assignments.Should().HaveCount(2);
        assignments.Should().OnlyContain(assignment => assignment.VolunteerProfileId == successor.Id);
        assignments.Select(assignment => assignment.StudentProfileId)
            .Should().BeEquivalentTo(new[] { shared.Id, onlyLeavers.Id });
    }

    [Fact]
    public async Task Volunteer_with_no_requests_is_deleted_and_assignments_go_with_them()
    {
        await using var context = CreateContext();
        var leaver = await SeedVolunteerAsync(context, "leaver@university.test");
        var student = await SeedStudentAsync(context);
        context.VolunteerAssignments.Add(Assignment(leaver.Id, student.Id));
        await context.SaveChangesAsync();

        var service = new VolunteerProvisioningService(
            UserManagerFor(leaver.User!).Object, context, Mock.Of<IAuditLogService>());

        var result = await service.RemoveAsync(leaver.Id, null);

        result.Succeeded.Should().BeTrue();
        (await context.VolunteerProfiles.CountAsync()).Should().Be(0);
        (await context.VolunteerAssignments.CountAsync()).Should().Be(0);
        (await context.StudentProfiles.CountAsync()).Should().Be(1, "the student is not being removed");
    }

    // ---------- helpers ----------

    private static VolunteerAssignment Assignment(int volunteerProfileId, int studentProfileId) => new()
    {
        VolunteerProfileId = volunteerProfileId,
        StudentProfileId = studentProfileId,
        Role = "Peer Mentor",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task<CounselorProfile> SeedCounselorAsync(ApplicationDbContext context, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            UserName = email,
            FullName = email,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var profile = new CounselorProfile { User = user, UserId = user.Id, CreatedAtUtc = DateTime.UtcNow };
        context.CounselorProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static async Task<VolunteerProfile> SeedVolunteerAsync(ApplicationDbContext context, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            UserName = email,
            FullName = email,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var profile = new VolunteerProfile
        {
            User = user,
            UserId = user.Id,
            IsApproved = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.VolunteerProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static async Task<StudentProfile> SeedStudentAsync(ApplicationDbContext context)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = $"student{Guid.NewGuid():N}@university.test",
            FullName = "Student",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var profile = new StudentProfile { User = user, UserId = user.Id, CreatedAtUtc = DateTime.UtcNow };
        context.StudentProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    private static async Task<CounselorBooking> SeedBookingAsync(
        ApplicationDbContext context,
        CounselorProfile counselor,
        StudentProfile student)
    {
        var booking = new CounselorBooking
        {
            CounselorProfileId = counselor.Id,
            StudentProfileId = student.Id,
            ScheduledForUtc = DateTime.UtcNow.AddDays(2),
            Status = BookingStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.CounselorBookings.Add(booking);
        await context.SaveChangesAsync();
        return booking;
    }

    private static Mock<UserManager<ApplicationUser>> UserManagerFor(ApplicationUser user)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            new Mock<IUserStore<ApplicationUser>>().Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        manager.Setup(m => m.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        return manager;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"staff-removal-{Guid.NewGuid()}")
            .Options);
}
