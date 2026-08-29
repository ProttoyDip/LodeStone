using Lodestone.Application.DTOs.Student;

namespace Lodestone.Application.Interfaces;

public interface IStudentNumberVerificationRepository
{
    Task<StudentNumberVerificationStateDto?> GetCurrentByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentNumberClaimDto>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VerifiedStudentNumberDto>> GetVerifiedAsync(
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> SubmitAsync(
        string userId,
        string normalizedStudentNumber,
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> ReviewAsync(
        int claimId,
        bool approve,
        string reviewerUserId,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> ResetAsync(
        int studentProfileId,
        string reviewerUserId,
        CancellationToken cancellationToken = default);
}
