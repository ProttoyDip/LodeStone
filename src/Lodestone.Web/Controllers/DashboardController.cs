using Lodestone.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

/// <summary>Renders analytics dashboards. Controllers call Application services only.</summary>
[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index()
    {
        if (User.IsInRole(RoleConstants.Admin))
            return RedirectToAction("Index", "Admin");

        if (User.IsInRole(RoleConstants.Counselor))
            return RedirectToAction("Queue", "Counselor");

        if (User.IsInRole(RoleConstants.Student))
            return RedirectToAction("Index", "Student");

        if (User.IsInRole(RoleConstants.Volunteer))
            return RedirectToAction("Index", "Forum");

        return Forbid();
    }
}
