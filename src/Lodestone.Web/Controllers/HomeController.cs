using Lodestone.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
    public IActionResult Privacy() => View();
    /// <summary>
    /// The single error page, reached both from the exception handler and from the status-code
    /// handler. It never renders exception detail.
    /// </summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error(int? statusCode)
    {
        var model = ErrorViewModel.ForStatusCode(statusCode);
        Response.StatusCode = model.StatusCode;
        return View(model);
    }
}
