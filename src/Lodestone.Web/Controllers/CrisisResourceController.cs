using Lodestone.Application.Interfaces;
using Lodestone.Web.ViewModels.CrisisResource;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

/// <summary>Public crisis resources page (no auth so anyone in distress can reach it).</summary>
public class CrisisResourceController : Controller
{
    private readonly ICrisisResourceService _crisisResourceService;

    public CrisisResourceController(ICrisisResourceService crisisResourceService)
        => _crisisResourceService = crisisResourceService;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await BuildAsync(null, searched: false, cancellationToken));

    /// <summary>
    /// Search is a POST so what a person writes never appears in a URL, browser history entry or
    /// access log. The text is ranked against the resources and discarded.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
        => View(await BuildAsync(q, searched: !string.IsNullOrWhiteSpace(q), cancellationToken));

    private async Task<CrisisResourceViewModel> BuildAsync(string? query, bool searched, CancellationToken cancellationToken)
    {
        var resources = await _crisisResourceService.GetActiveResourcesAsync(cancellationToken);
        var matches = searched
            ? await _crisisResourceService.FindResourcesAsync(query, cancellationToken)
            : Array.Empty<Lodestone.Application.DTOs.Crisis.CrisisResourceMatch>();

        // Emergency resources are never filtered by the search: a missed match must not hide them.
        return new CrisisResourceViewModel
        {
            EmergencyResources = resources.Where(resource => resource.IsEmergency).ToArray(),
            SupportResources = resources.Where(resource => !resource.IsEmergency).ToArray(),
            Query = searched ? query?.Trim() : null,
            Searched = searched,
            Matches = matches
        };
    }
}
