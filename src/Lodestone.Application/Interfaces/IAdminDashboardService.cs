using Lodestone.Application.DTOs.Admin;

namespace Lodestone.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<AdminShellDto> GetShellAsync(CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<AdminSectionPageDto> GetSectionAsync(
        AdminSectionType section,
        string? query = null,
        int page = 1,
        CancellationToken cancellationToken = default);
    Task<bool> MarkNotificationReadAsync(int id, CancellationToken cancellationToken = default);
    Task<int> MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default);
}
