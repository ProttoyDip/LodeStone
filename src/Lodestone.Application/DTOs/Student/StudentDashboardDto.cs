namespace Lodestone.Application.DTOs.Student;

/// <param name="Day">The student's own calendar day (a date with no zone attached), used for the chart label.</param>
public record StudentActivityDayDto(DateTime Day, int ActionCount);

public record StudentNextBookingDto(
    int Id,
    string CounselorName,
    DateTime StartUtc,
    DateTime EndUtc);

public record StudentRecommendationDto(
    string Eyebrow,
    string Title,
    string Detail,
    string Controller,
    string Action,
    string LinkLabel);

public record StudentDashboardDto(
    string DisplayName,
    bool HasJournalToday,
    int LoginCount,
    int JournalCount,
    int ForumInteractionCount,
    int BookingCount,
    IReadOnlyList<StudentActivityDayDto> ActivityDays,
    StudentNextBookingDto? NextBooking,
    StudentRecommendationDto Recommendation);
