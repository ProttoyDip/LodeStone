using Lodestone.Application.DTOs.Student;

namespace Lodestone.Application.Interfaces;

public interface IStudentNumberVerificationService
{
    Task<StudentNumberVerificationStateDto?> GetCurrentAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> SubmitAsync(
        string userId,
        string studentNumber,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentNumberClaimDto>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VerifiedStudentNumberDto>> GetVerifiedAsync(
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> ApproveAsync(
        int claimId,
        string reviewerUserId,
        string rowVersionToken,
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> RejectAsync(
        int claimId,
        string reviewerUserId,
        string rowVersionToken,
        CancellationToken cancellationToken = default);

    Task<StudentNumberClaimResultDto> ResetAsync(
        int studentProfileId,
        string reviewerUserId,
        CancellationToken cancellationToken = default);
}
