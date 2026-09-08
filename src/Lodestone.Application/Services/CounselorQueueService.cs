using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

public class CounselorQueueService : ICounselorQueueService
{
    private readonly ICounselorQueueRepository _repository;
    private readonly IRiskQueueNotifier _notifier;

    public CounselorQueueService(ICounselorQueueRepository repository, IRiskQueueNotifier notifier)
        => (_repository, _notifier) = (repository, notifier);

    public Task<IReadOnlyList<RiskQueueItemDto>> GetQueueAsync(CancellationToken cancellationToken = default)
        => _repository.GetOpenAsync(cancellationToken);

    public const int MaximumResolutionNoteLength = 300;

    public async Task<RiskQueueResolutionOutcome> TryResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        RiskCaseResolution resolution,
        string? resolutionNote,
        CancellationToken cancellationToken = default)
    {
        if (queueEntryId <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueEntryId));
        if (string.IsNullOrWhiteSpace(resolvedByUserId))
            throw new ArgumentException("The resolving user is required.", nameof(resolvedByUserId));
        if (!Enum.IsDefined(resolution) || resolution == RiskCaseResolution.Unspecified)
            throw new ArgumentOutOfRangeException(nameof(resolution), "Choose how the case was resolved.");
        if (string.IsNullOrWhiteSpace(rowVersionToken))
            return RiskQueueResolutionOutcome.ConcurrencyConflict;

        var note = resolutionNote?.Trim();
        if (string.IsNullOrEmpty(note)) note = null;
        else if (note.Length > MaximumResolutionNoteLength)
            throw new ArgumentException($"Notes cannot exceed {MaximumResolutionNoteLength} characters.", nameof(resolutionNote));

        var outcome = await _repository.ResolveAsync(
            queueEntryId,
            resolvedByUserId.Trim(),
            rowVersionToken,
            resolution,
            note,
            cancellationToken);
        if (outcome == RiskQueueResolutionOutcome.Resolved)
            await _notifier.NotifyChangedAsync(cancellationToken);
        return outcome;
    }
}
