using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Student;
using Lodestone.ML.Models;

namespace Lodestone.Web.ViewModels.Admin;

public sealed class RiskOperationsViewModel
{
    public required RiskModelStatus ModelStatus { get; init; }
    public RiskSnapshotStatusDto? SnapshotStatus { get; init; }
    public RiskSnapshotImportResultDto? ImportResult { get; init; }
    public string? StatusError { get; init; }
    public IReadOnlyList<StudentNumberClaimDto> PendingStudentNumberClaims { get; init; } = [];
    public IReadOnlyList<VerifiedStudentNumberDto> VerifiedStudentNumbers { get; init; } = [];
    public string? VerificationError { get; init; }

    public bool CanRunScoring =>
        ModelStatus.IsAvailable &&
        SnapshotStatus is { Model: not null, PendingSnapshotCount: > 0 };

    public string? ModelUnavailableReason =>
        SnapshotStatus?.ModelUnavailableReason ?? ModelStatus.UnavailableReason;

    public string ModelStateLabel => ModelStatus switch
    {
        { IsEnabled: false } => "Disabled",
        { IsAvailable: true } => "Available",
        _ => "Unavailable"
    };

    public string ModelStateTone => ModelStatus switch
    {
        { IsEnabled: false } => "info",
        { IsAvailable: true } => "positive",
        _ => "critical"
    };
}
