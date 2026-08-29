using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Hubs;

/// <summary>
/// Authorized, server-push-only endpoint for counselor queue notifications.
/// Queue mutations broadcast through IHubContext; clients cannot publish events.
/// </summary>
[Authorize(Policy = "CanViewRiskQueue")]
public class CounselorQueueHub : Hub
{
    public const string Route = "/hubs/counselor-queue";
}
