using Lodestone.Application.DTOs.Admin;

namespace Lodestone.Application.Interfaces;

public interface ICounselorProvisioningService
{
    Task<CounselorProvisioningResult> CreateAsync(CreateCounselorDto dto, CancellationToken cancellationToken = default);
    Task<CounselorProvisioningResult> CreateSetupTokenAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Counselors who can take over another counselor's appointments.</summary>
    Task<IReadOnlyList<StaffReplacementOptionDto>> GetReplacementsAsync(
        int excludingCounselorProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a counselor account. Appointments and their session reports are moved to
    /// <paramref name="replacementCounselorProfileId"/>, which is required whenever the counselor
    /// still has appointments.
    /// </summary>
    Task<StaffRemovalResult> RemoveAsync(
        int counselorProfileId,
        int? replacementCounselorProfileId,
        CancellationToken cancellationToken = default);
}
