using Lodestone.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lodestone.Web.Hubs;

[Authorize(Policy = PolicyConstants.CanAccessAdmin)]
public class AdminNotificationHub : Hub
{
    public const string Route = "/hubs/admin-notifications";
}
