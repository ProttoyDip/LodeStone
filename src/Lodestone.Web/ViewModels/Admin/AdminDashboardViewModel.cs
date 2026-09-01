using Lodestone.Application.DTOs.Admin;
using Lodestone.Web.ViewModels.Risk;

namespace Lodestone.Web.ViewModels.Admin;

public class AdminDashboardViewModel
{
    public AdminDashboardViewModel(AdminDashboardDto dashboard) => Dashboard = dashboard;

    public AdminDashboardDto Dashboard { get; }
    public RiskRuntimeStatusViewModel? RiskRuntime { get; init; }
}
