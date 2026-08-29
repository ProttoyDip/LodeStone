using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

public class CounselorQueueService : ICounselorQueueService
{
    private readonly ICounselorQueueRepository _repository;
    private readonly IRiskQueueNotifier _notifier;

    public CounselorQueueService(ICounselorQueueRepository repository, IRiskQueueNotifier notifier)
        => (_repository, _notifier) = (repository, notifier);

    public Task<IReadOnlyList<RiskQueueItemDto>> GetQueueAsync(CancellationToken cancellationToken = default)
        => _repository.GetOpenAsync(cancellationToken);

    public async Task<RiskQueueResolutionOutcome> TryResolveAsync(
        int queueEntryId,
        string resolvedByUserId,
        string? rowVersionToken,
        CancellationToken cancellationToken = default)
    {
        if (queueEntryId <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueEntryId));
        if (string.IsNullOrWhiteSpace(resolvedByUserId))
            throw new ArgumentException("The resolving user is required.", nameof(resolvedByUserId));
        if (string.IsNullOrWhiteSpace(rowVersionToken))
            return RiskQueueResolutionOutcome.ConcurrencyConflict;
        var outcome = await _repository.ResolveAsync(
            queueEntryId,
            resolvedByUserId.Trim(),
            rowVersionToken,
            cancellationToken);
        if (outcome == RiskQueueResolutionOutcome.Resolved)
            await _notifier.NotifyChangedAsync(cancellationToken);
        return outcome;
    }
}
