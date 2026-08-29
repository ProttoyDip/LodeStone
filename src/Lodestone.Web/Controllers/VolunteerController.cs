using Lodestone.Application.Interfaces;
using Lodestone.Domain.Constants;
using Lodestone.Domain.Entities;
using Lodestone.Web.ViewModels.Volunteer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Lodestone.Web.Controllers;

[AllowAnonymous]
public class VolunteerController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<VolunteerController> _logger;

    public VolunteerController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ICurrentUserService currentUserService,
        ILogger<VolunteerController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Become a Volunteer";
        return View(new VolunteerApplicationViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(VolunteerApplicationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Become a Volunteer";
            return View("Index", model);
        }

        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser is not null)
        {
            ModelState.AddModelError(string.Empty, "An account with that email already exists.");
            ViewData["Title"] = "Become a Volunteer";
            return View("Index", model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            VolunteerProfile = new VolunteerProfile
            {
                Bio = string.IsNullOrWhiteSpace(model.Bio) ? null : model.Bio.Trim(),
                IsApproved = false
            }
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewData["Title"] = "Become a Volunteer";
            return View("Index", model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, RoleConstants.Volunteer);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            foreach (var error in roleResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewData["Title"] = "Become a Volunteer";
            return View("Index", model);
        }

        _logger.LogInformation("New volunteer application created for {Email}.", model.Email);

        TempData["VolunteerSuccess"] = "Your volunteer application has been submitted. An administrator will review your profile shortly.";
        return RedirectToAction(nameof(Index));
    }
}
