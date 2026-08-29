using Lodestone.Application.DTOs.Admin;

namespace Lodestone.Application.Interfaces;

public interface ICounselorProvisioningService
{
    Task<CounselorProvisioningResult> CreateAsync(CreateCounselorDto dto, CancellationToken cancellationToken = default);
    Task<CounselorProvisioningResult> CreateSetupTokenAsync(string email, CancellationToken cancellationToken = default);
}
