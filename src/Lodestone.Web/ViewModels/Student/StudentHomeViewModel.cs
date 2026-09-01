using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.DTOs.Nudges;
using Lodestone.Domain.Enums;

namespace Lodestone.Web.ViewModels.Student;

public sealed record StudentHomeViewModel(
    StudentDashboardDto Dashboard,
    RiskMonitoringConsentDto? MonitoringConsent,
    StudentNumberVerificationStateDto? NumberVerification)
{
    /// <summary>
    /// Separate from risk-monitoring consent. These are counselor-authored, neutral in-app prompts.
    /// No ML state or score is included in this student-facing model.
    /// </summary>
    public StudentNudgeStateDto? NudgeState { get; init; }

    public string? NudgeLoadError { get; init; }

    public bool IsRiskMonitoringEnabled => MonitoringConsent?.IsConsented == true;

    public DateTime? ConsentChangedAtUtc => IsRiskMonitoringEnabled
        ? MonitoringConsent?.ConsentedAtUtc
        : MonitoringConsent?.WithdrawnAtUtc;

    public bool IsStudentNumberVerified => NumberVerification?.IsVerified == true;

    public bool CanSubmitStudentNumber =>
        !IsStudentNumberVerified && NumberVerification?.HasPendingClaim != true;

    public string StudentNumberStateLabel => NumberVerification switch
    {
        { IsVerified: true } => "Verified",
        { LatestClaim.Status: StudentNumberClaimStatus.Pending } => "Pending",
        { LatestClaim.Status: StudentNumberClaimStatus.Rejected } => "Rejected",
        _ => "Not submitted"
    };

    public string StudentNumberStateTone => NumberVerification switch
    {
        { IsVerified: true } => "verified",
        { LatestClaim.Status: StudentNumberClaimStatus.Pending } => "pending",
        { LatestClaim.Status: StudentNumberClaimStatus.Rejected } => "rejected",
        _ => "empty"
    };

    public string? DisplayStudentNumber => NumberVerification switch
    {
        { IsVerified: true } => NumberVerification.VerifiedStudentNumber,
        { LatestClaim.Status: StudentNumberClaimStatus.Pending or StudentNumberClaimStatus.Rejected } =>
            NumberVerification.LatestClaim.ClaimedStudentNumber,
        _ => null
    };
}
