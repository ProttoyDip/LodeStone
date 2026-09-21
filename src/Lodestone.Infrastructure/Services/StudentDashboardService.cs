using Lodestone.Application.Common;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class StudentDashboardService : IStudentDashboardService
{
    private readonly ApplicationDbContext _context;

    public StudentDashboardService(ApplicationDbContext context) => _context = context;

    public async Task<StudentDashboardDto?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await _context.StudentProfiles.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new
            {
                item.Id,
                Name = item.User != null ? item.User.FullName : string.Empty,
                TimeZoneId = item.User != null ? item.User.TimeZoneId : null
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return null;

        // The seven days on the chart are the student's own calendar days, not UTC days, so an evening
        // in Dhaka or New York lands on the day the student experienced it. Days are bounded by their
        // local midnights converted back to UTC, which is what the stored timestamps use.
        var zone = UserTime.Resolve(profile.TimeZoneId);
        DateTime LocalMidnightToUtc(DateTime localDay)
        {
            var local = DateTime.SpecifyKind(localDay, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) local = local.AddHours(1); // midnight skipped by a daylight-saving change
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        var todayUtc = DateTime.UtcNow.Date;
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        var firstDayLocal = todayLocal.AddDays(-6);
        var rangeStartUtc = LocalMidnightToUtc(firstDayLocal);
        var rangeEndUtc = LocalMidnightToUtc(todayLocal.AddDays(1));

        var loginEvents = await _context.ActivityLogs.AsNoTracking()
            .Where(item => item.StudentProfileId == profile.Id && item.OccurredAtUtc >= rangeStartUtc && item.OccurredAtUtc < rangeEndUtc)
            .Select(item => new { At = item.OccurredAtUtc, Count = item.LoginCount })
            .ToListAsync(cancellationToken);
        var journalEvents = await _context.MoodJournalEntries.AsNoTracking()
            .Where(item => item.StudentProfileId == profile.Id && !item.IsDeleted && item.EntryDateUtc >= rangeStartUtc && item.EntryDateUtc < rangeEndUtc)
            .Select(item => item.EntryDateUtc)
            .ToListAsync(cancellationToken);
        var postEvents = await _context.ForumPosts.AsNoTracking()
            .Where(item => item.AuthorUserId == userId && !item.IsDeleted && item.CreatedAtUtc >= rangeStartUtc && item.CreatedAtUtc < rangeEndUtc)
            .Select(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var commentEvents = await _context.ForumComments.AsNoTracking()
            .Where(item => item.AuthorUserId == userId && !item.IsDeleted && item.CreatedAtUtc >= rangeStartUtc && item.CreatedAtUtc < rangeEndUtc)
            .Select(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var bookingEvents = await _context.CounselorBookings.AsNoTracking()
            .Where(item => item.StudentProfileId == profile.Id && item.CreatedAtUtc >= rangeStartUtc && item.CreatedAtUtc < rangeEndUtc)
            .Select(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var nextBooking = await _context.CounselorBookings.AsNoTracking()
            .Where(item => item.StudentProfileId == profile.Id && item.Status == BookingStatus.Confirmed && item.ScheduledForUtc > DateTime.UtcNow)
            .OrderBy(item => item.ScheduledForUtc)
            .Select(item => new StudentNextBookingDto(
                item.Id,
                item.CounselorProfile != null && item.CounselorProfile.User != null && item.CounselorProfile.User.FullName != string.Empty
                    ? item.CounselorProfile.User.FullName
                    : "Counselor",
                item.ScheduledForUtc,
                item.AvailabilitySlot != null ? item.AvailabilitySlot.EndUtc : item.ScheduledForUtc.AddMinutes(50)))
            .FirstOrDefaultAsync(cancellationToken);

        var activityDays = Enumerable.Range(0, 7).Select(offset =>
        {
            var dayLocal = firstDayLocal.AddDays(offset);
            var day = LocalMidnightToUtc(dayLocal);
            var next = LocalMidnightToUtc(dayLocal.AddDays(1));
            var count = loginEvents.Where(item => item.At >= day && item.At < next).Sum(item => item.Count)
                        + journalEvents.Count(item => item >= day && item < next)
                        + postEvents.Count(item => item >= day && item < next)
                        + commentEvents.Count(item => item >= day && item < next)
                        + bookingEvents.Count(item => item >= day && item < next);
            return new StudentActivityDayDto(dayLocal, count);
        }).ToList().AsReadOnly();

        // The journal allows one entry per UTC day (enforced by a database index), so "today" here is
        // the UTC day even though the chart above follows the student's own days.
        var hasJournalToday = await _context.MoodJournalEntries.AsNoTracking().AnyAsync(
            item => item.StudentProfileId == profile.Id && !item.IsDeleted &&
                    item.EntryDateUtc >= todayUtc && item.EntryDateUtc < todayUtc.AddDays(1),
            cancellationToken);
        var recommendation = nextBooking is not null
            ? new StudentRecommendationDto("Next appointment", "Your counselor session is scheduled.", "Review the time or manage the appointment from booking.", "Booking", "Index", "View appointment")
            : !hasJournalToday
                ? new StudentRecommendationDto("Private check-in", "A short reflection is available today.", "Record how today feels without sharing it publicly.", "Journal", "Index", "Open journal")
                : postEvents.Count + commentEvents.Count == 0
                    ? new StudentRecommendationDto("Peer support", "The moderated community is available.", "Browse conversations or share only what feels useful.", "Forum", "Index", "Open forum")
                    : new StudentRecommendationDto("Support options", "Choose the support that fits the moment.", "Counselor booking and crisis resources remain within reach.", "Booking", "Index", "Explore booking");

        return new StudentDashboardDto(
            string.IsNullOrWhiteSpace(profile.Name) ? "there" : profile.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
            hasJournalToday,
            loginEvents.Sum(item => item.Count),
            journalEvents.Count,
            postEvents.Count + commentEvents.Count,
            bookingEvents.Count,
            activityDays,
            nextBooking,
            recommendation);
    }
}
