using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

[Authorize(Roles = RoleConstants.Student)]
public class StudentController : Controller
{
    private readonly IStudentDashboardService _dashboardService;
    private readonly ICurrentUserService _currentUserService;

    public StudentController(IStudentDashboardService dashboardService, ICurrentUserService currentUserService)
        => (_dashboardService, _currentUserService) = (dashboardService, currentUserService);

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUserService.UserId)) return Challenge();
        var dashboard = await _dashboardService.GetAsync(_currentUserService.UserId, cancellationToken);
        return dashboard is null ? Forbid() : View(dashboard);
    }
}
