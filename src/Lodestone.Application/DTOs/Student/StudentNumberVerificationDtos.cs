using Lodestone.Domain.Enums;

namespace Lodestone.Application.DTOs.Student;

public sealed record StudentNumberClaimDto(
    int Id,
    int StudentProfileId,
    string StudentName,
    string? StudentEmail,
    string ClaimedStudentNumber,
    StudentNumberClaimStatus Status,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewedByUserId,
    string RowVersionToken);

public sealed record StudentNumberVerificationStateDto(
    int StudentProfileId,
    string? VerifiedStudentNumber,
    StudentNumberClaimDto? LatestClaim)
{
    public bool IsVerified => !string.IsNullOrWhiteSpace(VerifiedStudentNumber);
    public bool HasPendingClaim => LatestClaim?.Status == StudentNumberClaimStatus.Pending;
}

public sealed record VerifiedStudentNumberDto(
    int StudentProfileId,
    string StudentName,
    string? StudentEmail,
    string StudentNumber,
    DateTime? VerifiedAtUtc);

public enum StudentNumberClaimOutcome
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Reset = 3,
    NotFound = 4,
    InvalidStudentNumber = 5,
    InvalidRequest = 6,
    PendingClaimExists = 7,
    AlreadyVerified = 8,
    AlreadyReviewed = 9,
    DuplicateStudentNumber = 10,
    ConcurrencyConflict = 11
}

public sealed record StudentNumberClaimResultDto(
    StudentNumberClaimOutcome Outcome,
    StudentNumberVerificationStateDto? State = null,
    StudentNumberClaimDto? Claim = null);
