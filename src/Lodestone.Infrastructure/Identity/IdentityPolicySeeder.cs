using Lodestone.Domain.Constants;
using Microsoft.AspNetCore.Authorization;

namespace Lodestone.Infrastructure.Identity;

/// <summary>Central definition of authorization policies mapping to roles.</summary>
public static class IdentityPolicySeeder
{
    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(PolicyConstants.CanViewRiskQueue, p =>
            p.RequireRole(RoleConstants.Counselor, RoleConstants.Admin));
        options.AddPolicy(PolicyConstants.CanManageBookings, p =>
            p.RequireRole(RoleConstants.Counselor, RoleConstants.Admin));
        options.AddPolicy(PolicyConstants.CanModerateForum, p =>
            p.RequireRole(RoleConstants.Volunteer, RoleConstants.Counselor, RoleConstants.Admin));
        options.AddPolicy(PolicyConstants.CanAccessAdmin, p =>
            p.RequireRole(RoleConstants.Admin));
        options.AddPolicy(PolicyConstants.CanManageVolunteers, p =>
            p.RequireRole(RoleConstants.Admin));
        options.AddPolicy(PolicyConstants.CanRequestPeerSupport, p =>
            p.RequireRole(RoleConstants.Student));
        options.AddPolicy(PolicyConstants.CanProvidePeerSupport, p =>
            p.RequireRole(RoleConstants.Volunteer));
    }
}
