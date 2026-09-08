using Lodestone.Application.DTOs.Nudges;

namespace Lodestone.Application.Interfaces;

public interface INudgeService
{
    Task<StudentNudgeStateDto?> GetForStudentAsync(string studentUserId, CancellationToken cancellationToken = default);
    Task<NudgeMutationResult> SetInAppPreferenceAsync(string studentUserId, bool enabled, CancellationToken cancellationToken = default);
    Task<NudgeMutationResult> RespondAsync(string studentUserId, int nudgeId, NudgeResponseAction action, CancellationToken cancellationToken = default);
    Task<NudgeMutationResult> CreateManualForBookingAsync(
        string counselorUserId,
        int bookingId,
        ManualNudgeTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>Outcomes of prompts this counselor sent, keyed by booking. Only status, never journal or risk data.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ManualNudgeOutcomeDto>>> GetManualOutcomesForCounselorAsync(
        string counselorUserId,
        CancellationToken cancellationToken = default);
    Task GenerateNudgesForAtRiskStudentsAsync(CancellationToken cancellationToken = default);
    Task DispatchPendingNudgesAsync(CancellationToken cancellationToken = default);
}
