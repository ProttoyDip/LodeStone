using Lodestone.Application.DTOs.Admin;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Creates peer-support volunteer accounts. Public registration only ever grants the Student role,
/// so this is the sole path that issues the Volunteer role together with its profile.
/// </summary>
public interface IVolunteerProvisioningService
{
    Task<VolunteerProvisioningResult> CreateAsync(
        CreateVolunteerDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Issues a fresh password-setup token for an existing volunteer account.</summary>
    Task<VolunteerProvisioningResult> CreateSetupTokenAsync(
        string email,
        CancellationToken cancellationToken = default);
}
