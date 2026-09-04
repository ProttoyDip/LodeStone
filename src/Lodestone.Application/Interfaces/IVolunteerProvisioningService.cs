using Lodestone.Application.DTOs.Admin;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Invites peer-support volunteers. Public registration only ever grants the Student role, so this
/// is the sole path that issues the Volunteer role.
/// </summary>
public interface IVolunteerProvisioningService
{
    /// <summary>
    /// Creates the account and grants the Volunteer role, then returns a password-setup token.
    /// No profile is created: the volunteer supplies their own details after signing in.
    /// </summary>
    Task<VolunteerProvisioningResult> InviteAsync(
        InviteVolunteerDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Issues a fresh password-setup token for an existing volunteer account.</summary>
    Task<VolunteerProvisioningResult> CreateSetupTokenAsync(
        string email,
        CancellationToken cancellationToken = default);
}
