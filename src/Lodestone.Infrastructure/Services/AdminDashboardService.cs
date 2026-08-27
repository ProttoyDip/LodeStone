using System.Globalization;
using Lodestone.Application.DTOs.Admin;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class AdminDashboardService : IAdminDashboardService
{
    private const int DashboardRowLimit = 8;
    private const int SectionRowLimit = 100;
    private const string AdminController = "Admin";

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditLogService _auditLog;

    public AdminDashboardService(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IAuditLogService auditLog)
    {
        _context = context;
        _currentUserService = currentUserService;
        _auditLog = auditLog;
    }

    public async Task<AdminShellDto> GetShellAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        var displayName = _currentUserService.UserName ?? "Admin";

        if (userId is not null)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(candidate => candidate.Id == userId)
                .Select(candidate => new { candidate.FullName, candidate.UserName })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is not null)
            {
                displayName = FirstNonEmpty(user.FullName, user.UserName, displayName);
            }
        }

        var unreadNotifications = userId is null
            ? 0
            : await _context.Notifications
                .AsNoTracking()
                .CountAsync(
                    notification => notification.RecipientUserId == userId && !notification.IsRead,
                    cancellationToken);

        return new AdminShellDto(
            AdminName: displayName,
            UnreadNotifications: unreadNotifications,
            CurrentDateLabel: DateTime.UtcNow.ToString("dddd, dd MMM yyyy", CultureInfo.InvariantCulture) + " UTC",
            ProfileImageUrl: "/images/admin-avatar.svg");
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var todayStartUtc = nowUtc.Date;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var shell = await GetShellAsync(cancellationToken);

        var openPriorityCases = await _context.RiskQueueEntries
            .AsNoTracking()
            .CountAsync(
                entry => !entry.IsResolved &&
                         (entry.Level == RiskLevel.High || entry.Level == RiskLevel.Critical),
                cancellationToken);

        var bookingsToday = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.ScheduledForUtc >= todayStartUtc &&
                           booking.ScheduledForUtc < tomorrowStartUtc &&
                           (booking.Status == BookingStatus.Requested || booking.Status == BookingStatus.Confirmed),
                cancellationToken);

        var moderationQueue = await _context.ForumPosts
            .AsNoTracking()
            .CountAsync(
                post => !post.IsDeleted &&
                        (post.Status == ForumPostStatus.Flagged ||
                         post.Status == ForumPostStatus.UnderReview ||
                         post.Flags.Any(flag => !flag.IsReviewed)),
                cancellationToken);

        var supportRows = (await BuildRiskRowsAsync(
            query: null,
            take: DashboardRowLimit,
            skip: 0,
            highOrCriticalOnly: true,
            cancellationToken)).Rows;

        var todayItems = await BuildTodayItemsAsync(
            bookingsToday,
            todayStartUtc,
            tomorrowStartUtc,
            cancellationToken);

        var riskTrend = await BuildRiskTrendAsync(todayStartUtc, tomorrowStartUtc, cancellationToken);
        var statusItems = await BuildStatusItemsAsync(
            openPriorityCases,
            moderationQueue,
            shell.UnreadNotifications,
            cancellationToken);
        var notificationRows = (await BuildNotificationRowsAsync(null, DashboardRowLimit, 0, cancellationToken)).Rows;

        var kpis = new[]
        {
            new AdminKpiDto(
                "Priority support cases",
                FormatNumber(openPriorityCases),
                "Unresolved high or critical cases",
                "bi-life-preserver",
                openPriorityCases > 0 ? "critical" : "positive",
                AdminController,
                "RiskMonitoring"),
            new AdminKpiDto(
                "Appointments today",
                FormatNumber(bookingsToday),
                "Requested or confirmed sessions",
                "bi-calendar2-check",
                bookingsToday > 0 ? "info" : "neutral",
                AdminController,
                "CounselorBookings"),
            new AdminKpiDto(
                "Moderation queue",
                FormatNumber(moderationQueue),
                "Flagged or under-review posts",
                "bi-shield-exclamation",
                moderationQueue > 0 ? "warning" : "positive",
                AdminController,
                "ForumModeration"),
            new AdminKpiDto(
                "Unread notifications",
                FormatNumber(shell.UnreadNotifications),
                "Notifications assigned to your account",
                "bi-bell",
                shell.UnreadNotifications > 0 ? "info" : "neutral",
                AdminController,
                "Notifications")
        };

        return new AdminDashboardDto(
            Shell: shell,
            Kpis: kpis,
            SupportColumns: DashboardSupportColumns(),
            SupportRows: supportRows,
            TodayItems: todayItems,
            RiskTrend: riskTrend,
            StatusItems: statusItems,
            NotificationColumns: DashboardNotificationColumns(),
            NotificationRows: notificationRows);
    }

    private static IReadOnlyList<AdminSectionColumnDto> DashboardSupportColumns()
        => new[]
        {
            new AdminSectionColumnDto("score", "Score", true),
            new AdminSectionColumnDto("level", "Risk level"),
            new AdminSectionColumnDto("queued", "Queued")
        };

    private static IReadOnlyList<AdminSectionColumnDto> DashboardNotificationColumns()
        => new[]
        {
            new AdminSectionColumnDto("type", "Type"),
            new AdminSectionColumnDto("received", "Received"),
            new AdminSectionColumnDto("status", "Status")
        };

    public Task<AdminSectionPageDto> GetSectionAsync(
        AdminSectionType section,
        string? query = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var normalizedPage = Math.Max(1, page);

        return section switch
        {
            AdminSectionType.RiskMonitoring => BuildRiskMonitoringPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.CounselorBookings => BuildBookingPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.ForumModeration => BuildForumModerationPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Students => BuildStudentsPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Counselors => BuildCounselorsPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Volunteers => BuildVolunteersPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Users => BuildUsersPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Notifications => BuildNotificationsPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.AuditLogs => BuildAuditLogsPageAsync(normalizedQuery, normalizedPage, cancellationToken),
            AdminSectionType.Profile => BuildProfilePageAsync(cancellationToken),
            AdminSectionType.Dashboard => Task.FromResult(BuildDashboardSection()),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown admin section.")
        };
    }

    public async Task<bool> MarkNotificationReadAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return false;
        }

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.RecipientUserId == userId,
                cancellationToken);

        if (notification is null)
        {
            return false;
        }

        if (notification.IsRead)
        {
            return true;
        }

        notification.IsRead = true;
        _auditLog.Record(
            action: "Notification.MarkRead",
            entityName: "Notification",
            entityId: id.ToString(CultureInfo.InvariantCulture));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return 0;
        }

        var updated = await _context.Notifications
            .Where(candidate => candidate.RecipientUserId == userId && !candidate.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.IsRead, true),
                cancellationToken);

        if (updated == 0)
        {
            return 0;
        }

        _auditLog.Record(
            action: "Notification.MarkAllRead",
            entityName: "Notification",
            details: $"Marked {updated} notification(s) as read.");
        await _context.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task<AdminSectionPageDto> BuildRiskMonitoringPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var openCounts = await _context.RiskQueueEntries
            .AsNoTracking()
            .Where(entry => !entry.IsResolved)
            .GroupBy(entry => entry.Level)
            .Select(group => new RiskLevelCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var resolvedToday = await _context.RiskQueueEntries
            .AsNoTracking()
            .CountAsync(
                entry => entry.IsResolved && entry.ResolvedAtUtc >= DateTime.UtcNow.Date,
                cancellationToken);

        var skip = (page - 1) * SectionRowLimit;
        var paged = await BuildRiskRowsAsync(query, SectionRowLimit, skip, false, cancellationToken);

        return new AdminSectionPageDto(
            Section: AdminSectionType.RiskMonitoring,
            Eyebrow: "Care operations",
            Title: "Support queue",
            Subtitle: "Review live risk cases that are waiting for follow-up. Assignment is not shown because it is not yet recorded by this system.",
            SearchPlaceholder: "Search by student name, email, or ID",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Critical open", FormatNumber(CountFor(openCounts, RiskLevel.Critical)), "critical"),
                new AdminMiniMetricDto("High open", FormatNumber(CountFor(openCounts, RiskLevel.High)), "warning"),
                new AdminMiniMetricDto("Moderate open", FormatNumber(CountFor(openCounts, RiskLevel.Moderate)), "info"),
                new AdminMiniMetricDto("Resolved today", FormatNumber(resolvedToday), "positive")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("student", "Student"),
                new AdminSectionColumnDto("studentId", "Student ID"),
                new AdminSectionColumnDto("score", "Score", true),
                new AdminSectionColumnDto("level", "Risk level"),
                new AdminSectionColumnDto("queued", "Queued"),
                new AdminSectionColumnDto("status", "Status")
            },
            Rows: paged.Rows,
            EmptyStateMessage: query is null
                ? "No unresolved support cases are currently in the queue."
                : "No support cases match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: paged.TotalCount);
    }

    private async Task<AdminSectionPageDto> BuildBookingPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var todayStartUtc = nowUtc.Date;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var sevenDaysAgoUtc = nowUtc.AddDays(-7);

        var requested = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(booking => booking.Status == BookingStatus.Requested, cancellationToken);
        var scheduledToday = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.ScheduledForUtc >= todayStartUtc &&
                           booking.ScheduledForUtc < tomorrowStartUtc &&
                           (booking.Status == BookingStatus.Requested || booking.Status == BookingStatus.Confirmed),
                cancellationToken);
        var upcomingConfirmed = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.Status == BookingStatus.Confirmed && booking.ScheduledForUtc >= nowUtc,
                cancellationToken);
        var completedRecently = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(
                booking => booking.Status == BookingStatus.Completed && booking.ScheduledForUtc >= sevenDaysAgoUtc,
                cancellationToken);

        var bookings = _context.CounselorBookings.AsNoTracking();
        if (query is not null)
        {
            bookings = bookings.Where(booking =>
                (booking.StudentProfile != null && booking.StudentProfile.User != null &&
                 (booking.StudentProfile.User.FullName.Contains(query) ||
                  (booking.StudentProfile.User.Email != null && booking.StudentProfile.User.Email.Contains(query)) ||
                  (booking.StudentProfile.StudentNumber != null && booking.StudentProfile.StudentNumber.Contains(query)))) ||
                (booking.CounselorProfile != null && booking.CounselorProfile.User != null &&
                 (booking.CounselorProfile.User.FullName.Contains(query) ||
                  (booking.CounselorProfile.User.Email != null && booking.CounselorProfile.User.Email.Contains(query)))));
        }

        var totalCount = await bookings.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await bookings
            .OrderByDescending(booking => booking.CreatedAtUtc)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(booking => new
            {
                booking.Id,
                StudentName = booking.StudentProfile != null && booking.StudentProfile.User != null
                    ? booking.StudentProfile.User.FullName
                    : string.Empty,
                StudentEmail = booking.StudentProfile != null && booking.StudentProfile.User != null
                    ? booking.StudentProfile.User.Email
                    : null,
                StudentNumber = booking.StudentProfile != null
                    ? booking.StudentProfile.StudentNumber
                    : null,
                CounselorName = booking.CounselorProfile != null && booking.CounselorProfile.User != null
                    ? booking.CounselorProfile.User.FullName
                    : string.Empty,
                CounselorEmail = booking.CounselorProfile != null && booking.CounselorProfile.User != null
                    ? booking.CounselorProfile.User.Email
                    : null,
                booking.ScheduledForUtc,
                booking.Status,
                booking.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(booking =>
        {
            var studentName = FirstNonEmpty(booking.StudentName, booking.StudentEmail, "Student");
            var counselorName = FirstNonEmpty(booking.CounselorName, booking.CounselorEmail, "Counselor");

            return new AdminSectionRowDto(
                Id: booking.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: studentName,
                SecondaryLabel: counselorName,
                BadgeText: BookingLabel(booking.Status),
                BadgeClass: BookingBadge(booking.Status),
                Cells: new Dictionary<string, string>
                {
                    ["student"] = studentName,
                    ["studentId"] = ValueOrFallback(booking.StudentNumber),
                    ["counselor"] = counselorName,
                    ["scheduled"] = FormatTimestamp(booking.ScheduledForUtc),
                    ["status"] = BookingLabel(booking.Status),
                    ["requested"] = FormatTimestamp(booking.CreatedAtUtc)
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.CounselorBookings,
            Eyebrow: "Care operations",
            Title: "Counselor bookings",
            Subtitle: "Monitor real appointment requests and confirmed sessions. Booking decisions remain with the counselor workflow.",
            SearchPlaceholder: "Search student or counselor",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Awaiting response", FormatNumber(requested), "warning"),
                new AdminMiniMetricDto("Scheduled today", FormatNumber(scheduledToday), "info"),
                new AdminMiniMetricDto("Upcoming confirmed", FormatNumber(upcomingConfirmed), "positive"),
                new AdminMiniMetricDto("Completed in 7 days", FormatNumber(completedRecently), "neutral")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("student", "Student"),
                new AdminSectionColumnDto("studentId", "Student ID"),
                new AdminSectionColumnDto("counselor", "Counselor"),
                new AdminSectionColumnDto("scheduled", "Scheduled for"),
                new AdminSectionColumnDto("status", "Status"),
                new AdminSectionColumnDto("requested", "Requested")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No counselor bookings have been recorded yet."
                : "No counselor bookings match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildForumModerationPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var flaggedPosts = await _context.ForumPosts
            .AsNoTracking()
            .CountAsync(
                post => !post.IsDeleted && post.Status == ForumPostStatus.Flagged,
                cancellationToken);
        var underReviewPosts = await _context.ForumPosts
            .AsNoTracking()
            .CountAsync(
                post => !post.IsDeleted && post.Status == ForumPostStatus.UnderReview,
                cancellationToken);
        var unreviewedFlags = await _context.ForumFlags
            .AsNoTracking()
            .CountAsync(
                flag => !flag.IsReviewed && flag.Post != null && !flag.Post.IsDeleted,
                cancellationToken);

        var posts = _context.ForumPosts
            .AsNoTracking()
            .Where(post => !post.IsDeleted &&
                           (post.Status == ForumPostStatus.Flagged ||
                            post.Status == ForumPostStatus.UnderReview ||
                            post.Flags.Any(flag => !flag.IsReviewed)));

        if (query is not null)
        {
            posts = posts.Where(post =>
                post.Title.Contains(query) ||
                post.AuthorUserId.Contains(query) ||
                (post.Category != null && post.Category.Name.Contains(query)));
        }

        var totalCount = await posts.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await posts
            .OrderByDescending(post => post.CreatedAtUtc)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(post => new
            {
                post.Id,
                post.Title,
                post.AuthorUserId,
                Category = post.Category != null ? post.Category.Name : string.Empty,
                post.Status,
                FlagCount = post.Flags.Count,
                UnreviewedFlagCount = post.Flags.Count(flag => !flag.IsReviewed),
                post.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var authorIds = records.Select(post => post.AuthorUserId).Distinct().ToList();
        var authors = authorIds.Count == 0
            ? new Dictionary<string, string>()
            : await _context.Users
                .AsNoTracking()
                .Where(user => authorIds.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => string.IsNullOrEmpty(user.FullName)
                        ? user.UserName ?? "Unknown user"
                        : user.FullName,
                    cancellationToken);

        var rows = records.Select(post =>
        {
            var status = post.Status == ForumPostStatus.Published && post.UnreviewedFlagCount > 0
                ? "Flagged"
                : ForumStatusLabel(post.Status);

            return new AdminSectionRowDto(
                Id: post.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: post.Title,
                SecondaryLabel: authors.GetValueOrDefault(post.AuthorUserId, "Unknown user"),
                BadgeText: status,
                BadgeClass: ForumBadge(post.Status, post.UnreviewedFlagCount),
                Cells: new Dictionary<string, string>
                {
                    ["post"] = post.Title,
                    ["author"] = authors.GetValueOrDefault(post.AuthorUserId, "Unknown user"),
                    ["category"] = ValueOrFallback(post.Category),
                    ["flags"] = FormatNumber(post.FlagCount),
                    ["unreviewed"] = FormatNumber(post.UnreviewedFlagCount),
                    ["status"] = status,
                    ["created"] = FormatTimestamp(post.CreatedAtUtc)
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.ForumModeration,
            Eyebrow: "Community safety",
            Title: "Forum moderation",
            Subtitle: "Review posts with active reports. Moderation decisions are available only through the dedicated review workflow.",
            SearchPlaceholder: "Search post, category, or author ID",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Flagged posts", FormatNumber(flaggedPosts), "critical"),
                new AdminMiniMetricDto("Under review", FormatNumber(underReviewPosts), "warning"),
                new AdminMiniMetricDto("Unreviewed reports", FormatNumber(unreviewedFlags), "info")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("post", "Post"),
                new AdminSectionColumnDto("author", "Author"),
                new AdminSectionColumnDto("category", "Category"),
                new AdminSectionColumnDto("flags", "Reports", true),
                new AdminSectionColumnDto("unreviewed", "Unreviewed", true),
                new AdminSectionColumnDto("status", "Status"),
                new AdminSectionColumnDto("created", "Created")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "There are no posts waiting for moderation."
                : "No moderation items match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildStudentsPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var totalStudents = await _context.StudentProfiles.AsNoTracking().CountAsync(cancellationToken);
        var activeStudents = await _context.StudentProfiles
            .AsNoTracking()
            .CountAsync(profile => profile.User != null && profile.User.IsActive, cancellationToken);
        var scoredStudents = await _context.RiskScores
            .AsNoTracking()
            .Select(score => score.StudentProfileId)
            .Distinct()
            .CountAsync(cancellationToken);

        var students = _context.StudentProfiles.AsNoTracking();
        if (query is not null)
        {
            students = students.Where(profile =>
                (profile.StudentNumber != null && profile.StudentNumber.Contains(query)) ||
                (profile.Program != null && profile.Program.Contains(query)) ||
                (profile.User != null &&
                 (profile.User.FullName.Contains(query) ||
                  (profile.User.Email != null && profile.User.Email.Contains(query)))));
        }

        var totalCount = await students.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await students
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.StudentNumber)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(profile => new
            {
                profile.Id,
                profile.StudentNumber,
                profile.Program,
                profile.EnrollmentYear,
                Name = profile.User != null ? profile.User.FullName : string.Empty,
                Email = profile.User != null ? profile.User.Email : null,
                IsActive = profile.User != null && profile.User.IsActive,
                LastLoginUtc = profile.User != null ? profile.User.LastLoginUtc : null,
                LatestRisk = profile.RiskScores
                    .OrderByDescending(score => score.ScoredAtUtc)
                    .Select(score => (RiskLevel?)score.Level)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(student =>
        {
            var name = FirstNonEmpty(student.Name, student.Email, student.StudentNumber, "Student");
            var riskLabel = student.LatestRisk.HasValue
                ? RiskLabel(student.LatestRisk.Value)
                : "Not scored";

            return new AdminSectionRowDto(
                Id: student.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: name,
                SecondaryLabel: ValueOrFallback(student.StudentNumber),
                BadgeText: riskLabel,
                BadgeClass: student.LatestRisk.HasValue ? RiskBadge(student.LatestRisk.Value) : "tone-neutral",
                Cells: new Dictionary<string, string>
                {
                    ["studentId"] = ValueOrFallback(student.StudentNumber),
                    ["name"] = name,
                    ["program"] = ValueOrFallback(student.Program),
                    ["year"] = student.EnrollmentYear > 0
                        ? student.EnrollmentYear.ToString(CultureInfo.InvariantCulture)
                        : "Not recorded",
                    ["email"] = ValueOrFallback(student.Email),
                    ["risk"] = riskLabel,
                    ["lastLogin"] = FormatOptionalTimestamp(student.LastLoginUtc),
                    ["status"] = student.IsActive ? "Active" : "Disabled"
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.Students,
            Eyebrow: "People and access",
            Title: "Students",
            Subtitle: "View student account and support context sourced from current platform records.",
            SearchPlaceholder: "Search student name, ID, email, or program",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Student profiles", FormatNumber(totalStudents), "neutral"),
                new AdminMiniMetricDto("Active accounts", FormatNumber(activeStudents), "positive"),
                new AdminMiniMetricDto("With a risk score", FormatNumber(scoredStudents), "info")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("name", "Name"),
                new AdminSectionColumnDto("studentId", "Student ID"),
                new AdminSectionColumnDto("program", "Program"),
                new AdminSectionColumnDto("year", "Enrollment year", true),
                new AdminSectionColumnDto("email", "Email"),
                new AdminSectionColumnDto("risk", "Latest risk"),
                new AdminSectionColumnDto("lastLogin", "Last sign-in"),
                new AdminSectionColumnDto("status", "Account")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No student profiles have been created yet."
                : "No students match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildCounselorsPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var totalCounselors = await _context.CounselorProfiles.AsNoTracking().CountAsync(cancellationToken);
        var acceptingBookings = await _context.CounselorProfiles
            .AsNoTracking()
            .CountAsync(profile => profile.IsAcceptingBookings, cancellationToken);
        var pendingBookings = await _context.CounselorBookings
            .AsNoTracking()
            .CountAsync(booking => booking.Status == BookingStatus.Requested, cancellationToken);

        var counselors = _context.CounselorProfiles.AsNoTracking();
        if (query is not null)
        {
            counselors = counselors.Where(profile =>
                (profile.Specialization != null && profile.Specialization.Contains(query)) ||
                (profile.User != null &&
                 (profile.User.FullName.Contains(query) ||
                  (profile.User.Email != null && profile.User.Email.Contains(query)))));
        }

        var totalCount = await counselors.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await counselors
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.Specialization)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(profile => new
            {
                profile.Id,
                profile.Specialization,
                profile.IsAcceptingBookings,
                Name = profile.User != null ? profile.User.FullName : string.Empty,
                Email = profile.User != null ? profile.User.Email : null,
                IsActive = profile.User != null && profile.User.IsActive,
                LastLoginUtc = profile.User != null ? profile.User.LastLoginUtc : null,
                RequestedBookings = profile.Bookings.Count(booking => booking.Status == BookingStatus.Requested),
                UpcomingSessions = profile.Bookings.Count(booking =>
                    booking.Status == BookingStatus.Confirmed && booking.ScheduledForUtc >= nowUtc),
                CompletedSessions = profile.Bookings.Count(booking => booking.Status == BookingStatus.Completed)
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(counselor =>
        {
            var name = FirstNonEmpty(counselor.Name, counselor.Email, "Counselor");
            var availability = counselor.IsAcceptingBookings ? "Accepting bookings" : "Not accepting";

            return new AdminSectionRowDto(
                Id: counselor.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: name,
                SecondaryLabel: ValueOrFallback(counselor.Specialization),
                BadgeText: availability,
                BadgeClass: counselor.IsAcceptingBookings ? "tone-positive" : "tone-neutral",
                Cells: new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["specialization"] = ValueOrFallback(counselor.Specialization),
                    ["availability"] = availability,
                    ["requests"] = FormatNumber(counselor.RequestedBookings),
                    ["upcoming"] = FormatNumber(counselor.UpcomingSessions),
                    ["completed"] = FormatNumber(counselor.CompletedSessions),
                    ["lastLogin"] = FormatOptionalTimestamp(counselor.LastLoginUtc),
                    ["account"] = counselor.IsActive ? "Active" : "Disabled"
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.Counselors,
            Eyebrow: "Care operations",
            Title: "Counselors",
            Subtitle: "Monitor current counselor availability and booking load without inventing capacity or response-time estimates.",
            SearchPlaceholder: "Search counselor or specialization",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Counselor profiles", FormatNumber(totalCounselors), "neutral"),
                new AdminMiniMetricDto("Accepting bookings", FormatNumber(acceptingBookings), "positive"),
                new AdminMiniMetricDto("Booking requests", FormatNumber(pendingBookings), "warning")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("name", "Counselor"),
                new AdminSectionColumnDto("specialization", "Specialization"),
                new AdminSectionColumnDto("availability", "Availability"),
                new AdminSectionColumnDto("requests", "Requests", true),
                new AdminSectionColumnDto("upcoming", "Upcoming", true),
                new AdminSectionColumnDto("completed", "Completed", true),
                new AdminSectionColumnDto("lastLogin", "Last sign-in"),
                new AdminSectionColumnDto("account", "Account")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No counselor profiles have been created yet."
                : "No counselors match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildVolunteersPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var totalVolunteers = await _context.VolunteerProfiles.AsNoTracking().CountAsync(cancellationToken);
        var approvedVolunteers = await _context.VolunteerProfiles
            .AsNoTracking()
            .CountAsync(profile => profile.IsApproved, cancellationToken);
        var activeVolunteers = await _context.VolunteerProfiles
            .AsNoTracking()
            .CountAsync(profile => profile.User != null && profile.User.IsActive, cancellationToken);

        var volunteers = _context.VolunteerProfiles.AsNoTracking();
        if (query is not null)
        {
            volunteers = volunteers.Where(profile =>
                (profile.Bio != null && profile.Bio.Contains(query)) ||
                (profile.User != null &&
                 (profile.User.FullName.Contains(query) ||
                  (profile.User.Email != null && profile.User.Email.Contains(query)))));
        }

        var totalCount = await volunteers.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await volunteers
            .OrderBy(profile => profile.User != null ? profile.User.FullName : profile.UserId)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(profile => new
            {
                profile.Id,
                profile.IsApproved,
                profile.CreatedAtUtc,
                Name = profile.User != null ? profile.User.FullName : string.Empty,
                Email = profile.User != null ? profile.User.Email : null,
                IsActive = profile.User != null && profile.User.IsActive,
                LastLoginUtc = profile.User != null ? profile.User.LastLoginUtc : null
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(volunteer =>
        {
            var name = FirstNonEmpty(volunteer.Name, volunteer.Email, "Volunteer");
            var approval = volunteer.IsApproved ? "Approved" : "Pending approval";

            return new AdminSectionRowDto(
                Id: volunteer.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: name,
                SecondaryLabel: ValueOrFallback(volunteer.Email),
                BadgeText: approval,
                BadgeClass: volunteer.IsApproved ? "tone-positive" : "tone-warning",
                Cells: new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["email"] = ValueOrFallback(volunteer.Email),
                    ["approval"] = approval,
                    ["joined"] = FormatDate(volunteer.CreatedAtUtc),
                    ["lastLogin"] = FormatOptionalTimestamp(volunteer.LastLoginUtc),
                    ["account"] = volunteer.IsActive ? "Active" : "Disabled"
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.Volunteers,
            Eyebrow: "Community",
            Title: "Volunteers",
            Subtitle: "Review volunteer approval and account status from live profile records.",
            SearchPlaceholder: "Search volunteer name or email",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Volunteer profiles", FormatNumber(totalVolunteers), "neutral"),
                new AdminMiniMetricDto("Approved", FormatNumber(approvedVolunteers), "positive"),
                new AdminMiniMetricDto("Active accounts", FormatNumber(activeVolunteers), "info")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("name", "Volunteer"),
                new AdminSectionColumnDto("email", "Email"),
                new AdminSectionColumnDto("approval", "Approval"),
                new AdminSectionColumnDto("joined", "Joined"),
                new AdminSectionColumnDto("lastLogin", "Last sign-in"),
                new AdminSectionColumnDto("account", "Account")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No volunteer profiles have been created yet."
                : "No volunteers match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildUsersPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var sevenDaysAgoUtc = nowUtc.AddDays(-7);
        var totalUsers = await _context.Users.AsNoTracking().CountAsync(cancellationToken);
        var activeUsers = await _context.Users
            .AsNoTracking()
            .CountAsync(user => user.IsActive, cancellationToken);
        var recentUsers = await _context.Users
            .AsNoTracking()
            .CountAsync(user => user.CreatedAtUtc >= sevenDaysAgoUtc, cancellationToken);

        var users = _context.Users.AsNoTracking();
        if (query is not null)
        {
            var matchingRoleUserIds = _context.UserRoles
                .Join(
                    _context.Roles,
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (userRole, role) => new { userRole.UserId, role.Name })
                .Where(item => item.Name != null && item.Name.Contains(query))
                .Select(item => item.UserId);

            users = users.Where(user =>
                user.FullName.Contains(query) ||
                (user.Email != null && user.Email.Contains(query)) ||
                (user.UserName != null && user.UserName.Contains(query)) ||
                matchingRoleUserIds.Contains(user.Id));
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await users
            .OrderBy(user => user.FullName)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(user => new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.UserName,
                user.IsActive,
                user.CreatedAtUtc,
                user.LastLoginUtc
            })
            .ToListAsync(cancellationToken);

        var userIds = records.Select(user => user.Id).ToList();
        var rolePairs = userIds.Count == 0
            ? new List<UserRoleRecord>()
            : await _context.UserRoles
                .AsNoTracking()
                .Where(userRole => userIds.Contains(userRole.UserId))
                .Join(
                    _context.Roles.AsNoTracking(),
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (userRole, role) => new UserRoleRecord(userRole.UserId, role.Name ?? "Unknown role"))
                .ToListAsync(cancellationToken);

        var rolesByUser = rolePairs
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(item => item.RoleName).OrderBy(role => role)));

        var rows = records.Select(user =>
        {
            var name = FirstNonEmpty(user.FullName, user.UserName, user.Email, "User");
            var roles = rolesByUser.GetValueOrDefault(user.Id, "No role assigned");

            return new AdminSectionRowDto(
                Id: user.Id,
                PrimaryLabel: name,
                SecondaryLabel: roles,
                BadgeText: user.IsActive ? "Active" : "Disabled",
                BadgeClass: user.IsActive ? "tone-positive" : "tone-critical",
                Cells: new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["roles"] = roles,
                    ["email"] = ValueOrFallback(user.Email),
                    ["username"] = ValueOrFallback(user.UserName),
                    ["created"] = FormatDate(user.CreatedAtUtc),
                    ["lastLogin"] = FormatOptionalTimestamp(user.LastLoginUtc),
                    ["status"] = user.IsActive ? "Active" : "Disabled"
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.Users,
            Eyebrow: "People and access",
            Title: "User accounts",
            Subtitle: "Inspect current identity accounts and assigned roles. Account editing is intentionally not offered until an audited workflow exists.",
            SearchPlaceholder: "Search name, email, username, or role",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Accounts", FormatNumber(totalUsers), "neutral"),
                new AdminMiniMetricDto("Active", FormatNumber(activeUsers), "positive"),
                new AdminMiniMetricDto("Disabled", FormatNumber(totalUsers - activeUsers), "critical"),
                new AdminMiniMetricDto("Created in 7 days", FormatNumber(recentUsers), "info")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("name", "Name"),
                new AdminSectionColumnDto("roles", "Roles"),
                new AdminSectionColumnDto("email", "Email"),
                new AdminSectionColumnDto("username", "Username"),
                new AdminSectionColumnDto("created", "Created"),
                new AdminSectionColumnDto("lastLogin", "Last sign-in"),
                new AdminSectionColumnDto("status", "Status")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No user accounts have been created yet."
                : "No user accounts match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildNotificationsPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var todayStartUtc = DateTime.UtcNow.Date;

        var total = userId is null
            ? 0
            : await _context.Notifications
                .AsNoTracking()
                .CountAsync(notification => notification.RecipientUserId == userId, cancellationToken);
        var unread = userId is null
            ? 0
            : await _context.Notifications
                .AsNoTracking()
                .CountAsync(
                    notification => notification.RecipientUserId == userId && !notification.IsRead,
                    cancellationToken);
        var receivedToday = userId is null
            ? 0
            : await _context.Notifications
                .AsNoTracking()
                .CountAsync(
                    notification => notification.RecipientUserId == userId &&
                                    notification.CreatedAtUtc >= todayStartUtc,
                    cancellationToken);

        var skip = (page - 1) * SectionRowLimit;
        var paged = await BuildNotificationRowsAsync(query, SectionRowLimit, skip, cancellationToken);

        return new AdminSectionPageDto(
            Section: AdminSectionType.Notifications,
            Eyebrow: "System",
            Title: "Notifications",
            Subtitle: "Messages addressed to your administrator account. Other users' notifications are never included here.",
            SearchPlaceholder: "Search your notifications",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Unread", FormatNumber(unread), unread > 0 ? "info" : "neutral"),
                new AdminMiniMetricDto("Received today", FormatNumber(receivedToday), "neutral"),
                new AdminMiniMetricDto("All notifications", FormatNumber(total), "neutral")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("title", "Notification"),
                new AdminSectionColumnDto("message", "Message"),
                new AdminSectionColumnDto("type", "Type"),
                new AdminSectionColumnDto("received", "Received"),
                new AdminSectionColumnDto("status", "Status")
            },
            Rows: paged.Rows,
            EmptyStateMessage: query is null
                ? "You do not have any notifications yet."
                : "No notifications match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: paged.TotalCount);
    }

    private async Task<AdminSectionPageDto> BuildAuditLogsPageAsync(
        string? query,
        int page,
        CancellationToken cancellationToken)
    {
        var todayStartUtc = DateTime.UtcNow.Date;
        var totalLogs = await _context.AuditLogs.AsNoTracking().CountAsync(cancellationToken);
        var logsToday = await _context.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.TimestampUtc >= todayStartUtc, cancellationToken);
        var actors = await _context.AuditLogs
            .AsNoTracking()
            .Where(log => log.UserId != null)
            .Select(log => log.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        var logs = _context.AuditLogs.AsNoTracking();
        if (query is not null)
        {
            logs = logs.Where(log =>
                log.Action.Contains(query) ||
                (log.EntityName != null && log.EntityName.Contains(query)) ||
                (log.EntityId != null && log.EntityId.Contains(query)) ||
                (log.Details != null && log.Details.Contains(query)) ||
                (log.UserId != null &&
                 (log.UserId.Contains(query) ||
                  _context.Users.Any(user =>
                      user.Id == log.UserId &&
                      (user.FullName.Contains(query) ||
                       (user.Email != null && user.Email.Contains(query)))))));
        }

        var totalCount = await logs.CountAsync(cancellationToken);
        var skip = (page - 1) * SectionRowLimit;

        var records = await logs
            .OrderByDescending(log => log.TimestampUtc)
            .Skip(skip)
            .Take(SectionRowLimit)
            .Select(log => new
            {
                log.Id,
                log.UserId,
                log.Action,
                log.EntityName,
                log.EntityId,
                log.Details,
                log.TimestampUtc
            })
            .ToListAsync(cancellationToken);

        var actorIds = records
            .Where(log => !string.IsNullOrWhiteSpace(log.UserId))
            .Select(log => log.UserId!)
            .Distinct()
            .ToList();
        var actorNames = actorIds.Count == 0
            ? new Dictionary<string, string>()
            : await _context.Users
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => string.IsNullOrEmpty(user.FullName)
                        ? user.UserName ?? user.Id
                        : user.FullName,
                    cancellationToken);

        var rows = records.Select(log =>
        {
            var actor = log.UserId is null
                ? "System"
                : actorNames.GetValueOrDefault(log.UserId, log.UserId);
            var entity = ValueOrFallback(log.EntityName);

            return new AdminSectionRowDto(
                Id: log.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: log.Action,
                SecondaryLabel: actor,
                BadgeText: entity,
                BadgeClass: "tone-neutral",
                Cells: new Dictionary<string, string>
                {
                    ["action"] = log.Action,
                    ["actor"] = actor,
                    ["entity"] = entity,
                    ["entityId"] = ValueOrFallback(log.EntityId),
                    ["details"] = Truncate(log.Details, 120),
                    ["timestamp"] = FormatTimestamp(log.TimestampUtc)
                });
        }).ToList();

        return new AdminSectionPageDto(
            Section: AdminSectionType.AuditLogs,
            Eyebrow: "System",
            Title: "Audit log",
            Subtitle: "Inspect the immutable trail of security and privacy-relevant events recorded by the platform.",
            SearchPlaceholder: "Search action, actor, entity, or details",
            Metrics: new[]
            {
                new AdminMiniMetricDto("Recorded events", FormatNumber(totalLogs), "neutral"),
                new AdminMiniMetricDto("Events today", FormatNumber(logsToday), "info"),
                new AdminMiniMetricDto("Recorded actors", FormatNumber(actors), "neutral")
            },
            Columns: new[]
            {
                new AdminSectionColumnDto("action", "Action"),
                new AdminSectionColumnDto("actor", "Actor"),
                new AdminSectionColumnDto("entity", "Entity"),
                new AdminSectionColumnDto("entityId", "Entity ID"),
                new AdminSectionColumnDto("details", "Details"),
                new AdminSectionColumnDto("timestamp", "Recorded")
            },
            Rows: rows,
            EmptyStateMessage: query is null
                ? "No audit events have been recorded yet."
                : "No audit events match this search.",
            Page: page,
            PageSize: SectionRowLimit,
            TotalCount: totalCount);
    }

    private async Task<AdminSectionPageDto> BuildProfilePageAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return new AdminSectionPageDto(
                AdminSectionType.Profile,
                "Account",
                "Administrator profile",
                "No authenticated administrator account is available in this request.",
                string.Empty,
                Array.Empty<AdminMiniMetricDto>(),
                ProfileColumns(),
                Array.Empty<AdminSectionRowDto>(),
                "No administrator profile is available.");
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.FullName,
                candidate.Email,
                candidate.UserName,
                candidate.IsActive,
                candidate.CreatedAtUtc,
                candidate.LastLoginUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return new AdminSectionPageDto(
                AdminSectionType.Profile,
                "Account",
                "Administrator profile",
                "The signed-in account could not be found in the current identity store.",
                string.Empty,
                Array.Empty<AdminMiniMetricDto>(),
                ProfileColumns(),
                Array.Empty<AdminSectionRowDto>(),
                "The administrator account record was not found.");
        }

        var roles = await _context.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Join(
                _context.Roles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (_, role) => role.Name ?? "Unknown role")
            .OrderBy(role => role)
            .ToListAsync(cancellationToken);

        var roleLabel = roles.Count == 0 ? "No role assigned" : string.Join(", ", roles);
        var name = FirstNonEmpty(user.FullName, user.UserName, user.Email, "Administrator");
        var accountStatus = user.IsActive ? "Active" : "Disabled";
        var row = new AdminSectionRowDto(
            Id: user.Id,
            PrimaryLabel: name,
            SecondaryLabel: roleLabel,
            BadgeText: accountStatus,
            BadgeClass: user.IsActive ? "tone-positive" : "tone-critical",
            Cells: new Dictionary<string, string>
            {
                ["name"] = name,
                ["email"] = ValueOrFallback(user.Email),
                ["username"] = ValueOrFallback(user.UserName),
                ["roles"] = roleLabel,
                ["created"] = FormatDate(user.CreatedAtUtc),
                ["lastLogin"] = FormatOptionalTimestamp(user.LastLoginUtc),
                ["status"] = accountStatus
            });

        return new AdminSectionPageDto(
            Section: AdminSectionType.Profile,
            Eyebrow: "Account",
            Title: "Administrator profile",
            Subtitle: "Your current identity record and access assignment.",
            SearchPlaceholder: string.Empty,
            Metrics: new[]
            {
                new AdminMiniMetricDto("Account status", accountStatus, user.IsActive ? "positive" : "critical"),
                new AdminMiniMetricDto("Roles", roleLabel, "info"),
                new AdminMiniMetricDto("Member since", FormatDate(user.CreatedAtUtc), "neutral"),
                new AdminMiniMetricDto("Last sign-in", FormatOptionalTimestamp(user.LastLoginUtc), "neutral")
            },
            Columns: ProfileColumns(),
            Rows: new[] { row },
            EmptyStateMessage: "The administrator account record was not found.",
            Page: 1,
            PageSize: 1,
            TotalCount: 1);
    }

    private async Task<PagedRows> BuildRiskRowsAsync(
        string? query,
        int take,
        int skip,
        bool highOrCriticalOnly,
        CancellationToken cancellationToken)
    {
        var entries = _context.RiskQueueEntries
            .AsNoTracking()
            .Where(entry => !entry.IsResolved);

        if (highOrCriticalOnly)
        {
            entries = entries.Where(entry =>
                entry.Level == RiskLevel.High || entry.Level == RiskLevel.Critical);
        }

        if (query is not null)
        {
            entries = entries.Where(entry =>
                entry.StudentProfile != null &&
                ((entry.StudentProfile.StudentNumber != null && entry.StudentProfile.StudentNumber.Contains(query)) ||
                 (entry.StudentProfile.User != null &&
                  (entry.StudentProfile.User.FullName.Contains(query) ||
                   (entry.StudentProfile.User.Email != null && entry.StudentProfile.User.Email.Contains(query))))));
        }

        var totalCount = await entries.CountAsync(cancellationToken);

        var records = await entries
            .OrderByDescending(entry => entry.Level)
            .ThenBy(entry => entry.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(entry => new
            {
                entry.Id,
                entry.Level,
                entry.CreatedAtUtc,
                StudentName = entry.StudentProfile != null && entry.StudentProfile.User != null
                    ? entry.StudentProfile.User.FullName
                    : string.Empty,
                StudentEmail = entry.StudentProfile != null && entry.StudentProfile.User != null
                    ? entry.StudentProfile.User.Email
                    : null,
                StudentNumber = entry.StudentProfile != null
                    ? entry.StudentProfile.StudentNumber
                    : null,
                Probability = entry.RiskScore != null ? (double?)entry.RiskScore.Probability : null,
                ScoredAtUtc = entry.RiskScore != null ? (DateTime?)entry.RiskScore.ScoredAtUtc : null
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(entry =>
        {
            var studentName = FirstNonEmpty(entry.StudentName, entry.StudentEmail, entry.StudentNumber, "Student");
            var level = RiskLabel(entry.Level);

            return new AdminSectionRowDto(
                Id: entry.Id.ToString(CultureInfo.InvariantCulture),
                PrimaryLabel: studentName,
                SecondaryLabel: ValueOrFallback(entry.StudentNumber),
                BadgeText: level,
                BadgeClass: RiskBadge(entry.Level),
                Cells: new Dictionary<string, string>
                {
                    ["student"] = studentName,
                    ["studentId"] = ValueOrFallback(entry.StudentNumber),
                    ["score"] = entry.Probability.HasValue
                        ? entry.Probability.Value.ToString("P1", CultureInfo.InvariantCulture)
                        : "Not recorded",
                    ["level"] = level,
                    ["queued"] = FormatTimestamp(entry.CreatedAtUtc),
                    ["scored"] = FormatOptionalTimestamp(entry.ScoredAtUtc),
                    ["status"] = "Awaiting review"
                });
        }).ToList();

        return new PagedRows(rows, totalCount);
    }

    private async Task<PagedRows> BuildNotificationRowsAsync(
        string? query,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return new PagedRows(Array.Empty<AdminSectionRowDto>(), 0);
        }

        var notifications = _context.Notifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId);

        if (query is not null)
        {
            notifications = notifications.Where(notification =>
                notification.Title.Contains(query) ||
                notification.Message.Contains(query));
        }

        var totalCount = await notifications.CountAsync(cancellationToken);

        var records = await notifications
            .OrderBy(notification => notification.IsRead)
            .ThenByDescending(notification => notification.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(notification => new
            {
                notification.Id,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.IsRead,
                notification.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var rows = records.Select(notification => new AdminSectionRowDto(
            Id: notification.Id.ToString(CultureInfo.InvariantCulture),
            PrimaryLabel: notification.Title,
            SecondaryLabel: notification.Message.Length <= 140
                ? notification.Message
                : Truncate(notification.Message, 140),
            BadgeText: notification.IsRead ? "Read" : "Unread",
            BadgeClass: notification.IsRead ? "tone-neutral" : "tone-info",
            Cells: new Dictionary<string, string>
            {
                ["title"] = notification.Title,
                ["message"] = Truncate(notification.Message, 120),
                ["type"] = NotificationLabel(notification.Type),
                ["received"] = FormatTimestamp(notification.CreatedAtUtc),
                ["status"] = notification.IsRead ? "Read" : "Unread"
            })).ToList();

        return new PagedRows(rows, totalCount);
    }

    private async Task<IReadOnlyList<AdminActivityItemDto>> BuildTodayItemsAsync(
        int bookingsToday,
        DateTime todayStartUtc,
        DateTime tomorrowStartUtc,
        CancellationToken cancellationToken)
    {
        var newPriorityScores = await _context.RiskScores
            .AsNoTracking()
            .CountAsync(
                score => score.ScoredAtUtc >= todayStartUtc &&
                         score.ScoredAtUtc < tomorrowStartUtc &&
                         (score.Level == RiskLevel.High || score.Level == RiskLevel.Critical),
                cancellationToken);
        var newForumFlags = await _context.ForumFlags
            .AsNoTracking()
            .CountAsync(
                flag => flag.CreatedAtUtc >= todayStartUtc && flag.CreatedAtUtc < tomorrowStartUtc,
                cancellationToken);
        var newAccounts = await _context.Users
            .AsNoTracking()
            .CountAsync(
                user => user.CreatedAtUtc >= todayStartUtc && user.CreatedAtUtc < tomorrowStartUtc,
                cancellationToken);

        var items = new List<AdminActivityItemDto>();
        if (newPriorityScores > 0)
        {
            items.Add(new AdminActivityItemDto(
                "New priority risk scores",
                $"{FormatNumber(newPriorityScores)} high or critical score{PluralSuffix(newPriorityScores)} recorded",
                "Today",
                "bi-activity",
                "critical"));
        }

        if (bookingsToday > 0)
        {
            items.Add(new AdminActivityItemDto(
                "Counselor appointments",
                $"{FormatNumber(bookingsToday)} requested or confirmed session{PluralSuffix(bookingsToday)} scheduled",
                "Today",
                "bi-calendar2-check",
                "info"));
        }

        if (newForumFlags > 0)
        {
            items.Add(new AdminActivityItemDto(
                "New forum reports",
                $"{FormatNumber(newForumFlags)} moderation report{PluralSuffix(newForumFlags)} received",
                "Today",
                "bi-flag",
                "warning"));
        }

        if (newAccounts > 0)
        {
            items.Add(new AdminActivityItemDto(
                "New accounts",
                $"{FormatNumber(newAccounts)} account{PluralSuffix(newAccounts)} created",
                "Today",
                "bi-person-plus",
                "neutral"));
        }

        return items;
    }

    private async Task<IReadOnlyList<AdminTrendPointDto>> BuildRiskTrendAsync(
        DateTime todayStartUtc,
        DateTime tomorrowStartUtc,
        CancellationToken cancellationToken)
    {
        var firstDayUtc = todayStartUtc.AddDays(-6);
        var buckets = await _context.RiskScores
            .AsNoTracking()
            .Where(score => score.ScoredAtUtc >= firstDayUtc && score.ScoredAtUtc < tomorrowStartUtc)
            .GroupBy(score => new { Day = score.ScoredAtUtc.Date, score.Level })
            .Select(group => new RiskTrendBucket(group.Key.Day, group.Key.Level, group.Count()))
            .ToListAsync(cancellationToken);

        if (buckets.Count == 0)
        {
            return Array.Empty<AdminTrendPointDto>();
        }

        var trend = new List<AdminTrendPointDto>(7);
        for (var offset = 0; offset < 7; offset++)
        {
            var day = firstDayUtc.AddDays(offset);
            var low = CountFor(buckets, day, RiskLevel.Low);
            var moderate = CountFor(buckets, day, RiskLevel.Moderate);
            var high = CountFor(buckets, day, RiskLevel.High);
            var critical = CountFor(buckets, day, RiskLevel.Critical);

            trend.Add(new AdminTrendPointDto(
                Label: day.ToString("ddd", CultureInfo.InvariantCulture),
                Low: low,
                Moderate: moderate,
                High: high,
                Critical: critical,
                Total: low + moderate + high + critical));
        }

        return trend;
    }

    private async Task<IReadOnlyList<AdminStatusItemDto>> BuildStatusItemsAsync(
        int openPriorityCases,
        int moderationQueue,
        int unreadNotifications,
        CancellationToken cancellationToken)
    {
        var latestRiskScore = await _context.RiskScores
            .AsNoTracking()
            .OrderByDescending(score => score.ScoredAtUtc)
            .Select(score => (DateTime?)score.ScoredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var oldestOpenCase = await _context.RiskQueueEntries
            .AsNoTracking()
            .Where(entry => !entry.IsResolved)
            .OrderBy(entry => entry.CreatedAtUtc)
            .Select(entry => (DateTime?)entry.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var latestAuditEvent = await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.TimestampUtc)
            .Select(log => (DateTime?)log.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var userId = CurrentUserId;
        var latestNotification = userId is null
            ? null
            : await _context.Notifications
                .AsNoTracking()
                .Where(notification => notification.RecipientUserId == userId)
                .OrderByDescending(notification => notification.CreatedAtUtc)
                .Select(notification => (DateTime?)notification.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        return new[]
        {
            new AdminStatusItemDto(
                "Risk data",
                latestRiskScore.HasValue ? FormatTimestamp(latestRiskScore.Value) : "No scores recorded",
                "Most recent stored risk score",
                latestRiskScore.HasValue ? "info" : "neutral"),
            new AdminStatusItemDto(
                "Support queue",
                $"{FormatNumber(openPriorityCases)} priority open",
                oldestOpenCase.HasValue
                    ? $"Oldest unresolved case: {FormatTimestamp(oldestOpenCase.Value)}"
                    : "No unresolved cases",
                openPriorityCases > 0 ? "warning" : "positive"),
            new AdminStatusItemDto(
                "Moderation",
                $"{FormatNumber(moderationQueue)} waiting",
                "Posts flagged, under review, or carrying an unreviewed report",
                moderationQueue > 0 ? "warning" : "positive"),
            new AdminStatusItemDto(
                "Audit trail",
                latestAuditEvent.HasValue ? FormatTimestamp(latestAuditEvent.Value) : "No events recorded",
                "Most recent stored audit event",
                latestAuditEvent.HasValue ? "neutral" : "warning"),
            new AdminStatusItemDto(
                "Your notifications",
                $"{FormatNumber(unreadNotifications)} unread",
                latestNotification.HasValue
                    ? $"Most recent: {FormatTimestamp(latestNotification.Value)}"
                    : "No notifications received",
                unreadNotifications > 0 ? "info" : "neutral")
        };
    }

    private static AdminSectionPageDto BuildDashboardSection()
        => new(
            AdminSectionType.Dashboard,
            "Overview",
            "Admin dashboard",
            "Use the dashboard overview for live care, community, and system signals.",
            string.Empty,
            Array.Empty<AdminMiniMetricDto>(),
            Array.Empty<AdminSectionColumnDto>(),
            Array.Empty<AdminSectionRowDto>(),
            "Dashboard data is shown on the overview page.");

    private static IReadOnlyList<AdminSectionColumnDto> ProfileColumns()
        => new[]
        {
            new AdminSectionColumnDto("name", "Name"),
            new AdminSectionColumnDto("email", "Email"),
            new AdminSectionColumnDto("username", "Username"),
            new AdminSectionColumnDto("roles", "Roles"),
            new AdminSectionColumnDto("created", "Created"),
            new AdminSectionColumnDto("lastLogin", "Last sign-in"),
            new AdminSectionColumnDto("status", "Status")
        };

    private string? CurrentUserId
        => _currentUserService.IsAuthenticated && !string.IsNullOrWhiteSpace(_currentUserService.UserId)
            ? _currentUserService.UserId
            : null;

    private static string? NormalizeQuery(string? query)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static int CountFor(IEnumerable<RiskLevelCount> source, RiskLevel level)
        => source.Where(item => item.Level == level).Sum(item => item.Count);

    private static int CountFor(
        IEnumerable<RiskTrendBucket> buckets,
        DateTime day,
        RiskLevel level)
        => buckets
            .Where(item => item.Day == day.Date && item.Level == level)
            .Sum(item => item.Count);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ValueOrFallback(string? value, string fallback = "Not recorded")
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatNumber(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime value)
        => value == default
            ? "Not recorded"
            : value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTime value)
        => value == default
            ? "Not recorded"
            : $"{value:dd MMM yyyy, HH:mm} UTC";

    private static string FormatOptionalTimestamp(DateTime? value)
        => value.HasValue ? FormatTimestamp(value.Value) : "Not recorded";

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not recorded";
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maximumLength - 1), "…");
    }

    private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";

    private static string RiskLabel(RiskLevel level)
        => level switch
        {
            RiskLevel.Critical => "Critical",
            RiskLevel.High => "High",
            RiskLevel.Moderate => "Moderate",
            _ => "Low"
        };

    private static string RiskBadge(RiskLevel level)
        => level switch
        {
            RiskLevel.Critical => "tone-critical",
            RiskLevel.High => "tone-warning",
            RiskLevel.Moderate => "tone-info",
            _ => "tone-positive"
        };

    private static string BookingLabel(BookingStatus status)
        => status switch
        {
            BookingStatus.Requested => "Requested",
            BookingStatus.Confirmed => "Confirmed",
            BookingStatus.Completed => "Completed",
            BookingStatus.Cancelled => "Cancelled",
            BookingStatus.NoShow => "No show",
            _ => status.ToString()
        };

    private static string BookingBadge(BookingStatus status)
        => status switch
        {
            BookingStatus.Requested => "tone-warning",
            BookingStatus.Confirmed => "tone-info",
            BookingStatus.Completed => "tone-positive",
            BookingStatus.Cancelled or BookingStatus.NoShow => "tone-critical",
            _ => "tone-neutral"
        };

    private static string ForumStatusLabel(ForumPostStatus status)
        => status switch
        {
            ForumPostStatus.Published => "Published",
            ForumPostStatus.Flagged => "Flagged",
            ForumPostStatus.UnderReview => "Under review",
            ForumPostStatus.Removed => "Removed",
            _ => status.ToString()
        };

    private static string ForumBadge(ForumPostStatus status, int unreviewedFlags)
        => status switch
        {
            ForumPostStatus.Flagged => "tone-critical",
            ForumPostStatus.UnderReview => "tone-warning",
            ForumPostStatus.Published when unreviewedFlags > 0 => "tone-warning",
            ForumPostStatus.Removed => "tone-neutral",
            _ => "tone-info"
        };

    private static string NotificationLabel(NotificationType type)
        => type switch
        {
            NotificationType.RiskAlert => "Risk alert",
            NotificationType.Nudge => "Nudge",
            NotificationType.BookingReminder => "Booking reminder",
            NotificationType.ForumReply => "Forum reply",
            NotificationType.CrisisEscalation => "Crisis escalation",
            NotificationType.System => "System",
            _ => type.ToString()
        };

    private sealed record PagedRows(IReadOnlyList<AdminSectionRowDto> Rows, int TotalCount);
    private sealed record UserRoleRecord(string UserId, string RoleName);
    private sealed record RiskLevelCount(RiskLevel Level, int Count);
    private sealed record RiskTrendBucket(DateTime Day, RiskLevel Level, int Count);
}
