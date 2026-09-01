using Lodestone.Application.DTOs.Journal;
using Lodestone.Application.Exceptions;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Services;

public class JournalService : IJournalService
{
    private readonly IJournalRepository _journalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISensitiveDataProtector _sensitiveDataProtector;

    public JournalService(
        IJournalRepository journalRepository,
        IUnitOfWork unitOfWork,
        ISensitiveDataProtector sensitiveDataProtector)
    {
        _journalRepository = journalRepository;
        _unitOfWork = unitOfWork;
        _sensitiveDataProtector = sensitiveDataProtector;
    }

    public async Task<IReadOnlyList<JournalEntryDto>> GetEntriesAsync(
        int studentProfileId, CancellationToken cancellationToken = default)
    {
        var entries = await _journalRepository.GetByStudentIdAsync(studentProfileId, cancellationToken);
        return entries
            .Select(e => new JournalEntryDto(
                e.Id,
                e.StudentProfileId,
                e.MoodRating,
                e.Note is null ? null : _sensitiveDataProtector.Unprotect(e.Note),
                e.EntryDateUtc))
            .ToList()
            .AsReadOnly();
    }

    public async Task<JournalEntryDto> AddEntryAsync(
        int studentProfileId, CreateJournalEntryDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(studentProfileId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dto.MoodRating, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dto.MoodRating, 5);
        if (dto.Note?.Length > 2000)
            throw new ArgumentException("Journal notes cannot exceed 2000 characters.", nameof(dto));

        var now = DateTime.UtcNow;
        var dayStartUtc = now.Date;
        if (await _journalRepository.HasEntryForDayAsync(studentProfileId, dayStartUtc, cancellationToken))
            throw new DailyJournalEntryLimitException();

        var plaintextNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        var entry = new MoodJournalEntry
        {
            StudentProfileId = studentProfileId,
            MoodRating = dto.MoodRating,
            Note = plaintextNote is null ? null : _sensitiveDataProtector.Protect(plaintextNote),
            NoteProtectionVersion = 1,
            EntryDateUtc = now,
            CreatedAtUtc = now,
        };

        await _journalRepository.AddAsync(entry, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new JournalEntryDto(entry.Id, entry.StudentProfileId, entry.MoodRating, plaintextNote, entry.EntryDateUtc);
    }
}
