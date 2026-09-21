using System.Security.Claims;
using Lodestone.Application.Interfaces;

namespace Lodestone.Web.Middleware;

/// <summary>
/// Marks the signed-in account as recently active. A failure to record presence must never break
/// the request it rides on, so errors are logged and swallowed.
/// </summary>
public sealed class UserPresenceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserPresenceMiddleware> _logger;

    public UserPresenceMiddleware(RequestDelegate next, ILogger<UserPresenceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserPresenceService presence)
    {
        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        if (!string.IsNullOrEmpty(userId))
        {
            try
            {
                await presence.TouchAsync(userId, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not record presence for the signed-in account.");
            }
        }

        await _next(context);
    }
}
