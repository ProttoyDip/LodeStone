namespace Lodestone.Application.DTOs.Student;

public record StudentActivityDayDto(DateTime DayUtc, int ActionCount);

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
