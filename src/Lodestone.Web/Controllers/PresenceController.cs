using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lodestone.Web.Controllers;

/// <summary>
/// Heartbeat target. The presence middleware records activity for any authenticated request, so an
/// open page only has to hit this endpoint periodically to stay listed as online.
/// </summary>
[Authorize]
[Route("Presence")]
public sealed class PresenceController : Controller
{
    [HttpPost("Ping")]
    [IgnoreAntiforgeryToken]
    public IActionResult Ping() => NoContent();
}
