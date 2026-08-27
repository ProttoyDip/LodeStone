namespace Lodestone.Application.DTOs.Admin;

public enum AdminSectionType
{
    Dashboard,
    RiskMonitoring,
    CounselorBookings,
    ForumModeration,
    Students,
    Counselors,
    Volunteers,
    Users,
    Notifications,
    AuditLogs,
    Profile
}

public record AdminShellDto(
    string AdminName,
    int UnreadNotifications,
    string CurrentDateLabel,
    string ProfileImageUrl);

public record AdminKpiDto(
    string Label,
    string Value,
    string Detail,
    string Icon,
    string Tone,
    string Controller,
    string Action);

public record AdminMiniMetricDto(string Label, string Value, string? Tone = null);

public record AdminActivityItemDto(
    string Title,
    string Detail,
    string TimeLabel,
    string Icon,
    string Tone);

public record AdminTrendPointDto(
    string Label,
    int Low,
    int Moderate,
    int High,
    int Critical,
    int Total);

public record AdminStatusItemDto(
    string Label,
    string Value,
    string Detail,
    string Tone);

public record AdminSectionColumnDto(
    string Key,
    string Label,
    bool IsNumeric = false,
    string? CssClass = null);

public record AdminSectionRowDto(
    string Id,
    string PrimaryLabel,
    string SecondaryLabel,
    string? BadgeText,
    string? BadgeClass,
    IReadOnlyDictionary<string, string> Cells);

public record AdminSectionPageDto(
    AdminSectionType Section,
    string Eyebrow,
    string Title,
    string Subtitle,
    string SearchPlaceholder,
    IReadOnlyList<AdminMiniMetricDto> Metrics,
    IReadOnlyList<AdminSectionColumnDto> Columns,
    IReadOnlyList<AdminSectionRowDto> Rows,
    string EmptyStateMessage,
    int Page = 1,
    int PageSize = 100,
    int TotalCount = 0);

public record AdminDashboardDto(
    AdminShellDto Shell,
    IReadOnlyList<AdminKpiDto> Kpis,
    IReadOnlyList<AdminSectionColumnDto> SupportColumns,
    IReadOnlyList<AdminSectionRowDto> SupportRows,
    IReadOnlyList<AdminActivityItemDto> TodayItems,
    IReadOnlyList<AdminTrendPointDto> RiskTrend,
    IReadOnlyList<AdminStatusItemDto> StatusItems,
    IReadOnlyList<AdminSectionColumnDto> NotificationColumns,
    IReadOnlyList<AdminSectionRowDto> NotificationRows);
