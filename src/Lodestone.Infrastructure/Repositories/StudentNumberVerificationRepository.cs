using System.Data;
using System.Globalization;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.DTOs.Student;
using Lodestone.Application.Interfaces;
using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lodestone.Infrastructure.Repositories;

/// <summary>
/// Persists student-number claims and makes approval/reset privacy transitions atomic.
/// </summary>
public sealed class StudentNumberVerificationRepository : IStudentNumberVerificationRepository
{
    private const string PendingClaimIndex = "UX_StudentNumberClaims_OnePendingPerStudent";
    private const string VerifiedStudentNumberIndex = "UX_StudentProfiles_StudentNumber";

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public StudentNumberVerificationRepository(ApplicationDbContext context, TimeProvider timeProvider)
        => (_context, _timeProvider) = (context, timeProvider);

    public async Task<StudentNumberVerificationStateDto?> GetCurrentByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.StudentProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new ProfileProjection(
                item.Id,
                item.User != null ? item.User.FullName : null,
                item.User != null ? item.User.Email : null,
                item.StudentNumber))
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return null;

        var latestClaim = await _context.StudentNumberClaims
            .AsNoTracking()
            .Where(claim => claim.StudentProfileId == profile.Id)
            .OrderByDescending(claim => claim.SubmittedAtUtc)
            .ThenByDescending(claim => claim.Id)
            .Select(claim => new ClaimProjection(
                claim.Id,
                claim.StudentProfileId,
                claim.ClaimedStudentNumber,
                claim.Status,
                claim.SubmittedAtUtc,
                claim.ReviewedAtUtc,
                claim.ReviewedByUserId,
                claim.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);

        return ToState(profile, latestClaim);
    }

    public async Task<IReadOnlyList<StudentNumberClaimDto>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var claims = await _context.StudentNumberClaims
            .AsNoTracking()
            .Where(claim => claim.Status == StudentNumberClaimStatus.Pending)
            .OrderBy(claim => claim.SubmittedAtUtc)
            .ThenBy(claim => claim.Id)
            .Select(claim => new
            {
                Claim = new ClaimProjection(
                    claim.Id,
                    claim.StudentProfileId,
                    claim.ClaimedStudentNumber,
                    claim.Status,
                    claim.SubmittedAtUtc,
                    claim.ReviewedAtUtc,
                    claim.ReviewedByUserId,
                    claim.RowVersion),
                StudentName = claim.StudentProfile != null && claim.StudentProfile.User != null
                    ? claim.StudentProfile.User.FullName
                    : null,
                StudentEmail = claim.StudentProfile != null && claim.StudentProfile.User != null
                    ? claim.StudentProfile.User.Email
                    : null
            })
            .ToListAsync(cancellationToken);

        return claims
            .Select(item => ToClaimDto(item.Claim, item.StudentName, item.StudentEmail))
            .ToArray();
    }

    public async Task<IReadOnlyList<VerifiedStudentNumberDto>> GetVerifiedAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.StudentNumber != null)
            .Select(profile => new VerifiedStudentNumberDto(
                profile.Id,
                profile.User != null && !string.IsNullOrWhiteSpace(profile.User.FullName)
                    ? profile.User.FullName
                    : "Student",
                profile.User != null ? profile.User.Email : null,
                profile.StudentNumber!,
                profile.StudentNumberClaims
                    .Where(claim =>
                        claim.Status == StudentNumberClaimStatus.Approved &&
                        claim.ClaimedStudentNumber == profile.StudentNumber)
                    .OrderByDescending(claim => claim.ReviewedAtUtc)
                    .Select(claim => claim.ReviewedAtUtc)
                    .FirstOrDefault()))
            .OrderBy(item => item.StudentName)
            .ThenBy(item => item.StudentNumber)
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<StudentNumberClaimResultDto> SubmitAsync(
        string userId,
        string normalizedStudentNumber,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var profile = await _context.StudentProfiles
                .Include(item => item.User)
                .Include(item => item.StudentNumberClaims.Where(
                    claim => claim.Status == StudentNumberClaimStatus.Pending))
                .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            if (profile is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.NotFound);
            }

            if (!string.IsNullOrWhiteSpace(profile.StudentNumber))
            {
                var state = await BuildStateAsync(profile, cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(
                    StudentNumberClaimOutcome.AlreadyVerified,
                    state);
            }

            var pending = profile.StudentNumberClaims.SingleOrDefault(
                claim => claim.Status == StudentNumberClaimStatus.Pending);
            if (pending is not null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                var pendingDto = ToClaimDto(pending, profile.User);
                return new StudentNumberClaimResultDto(
                    StudentNumberClaimOutcome.PendingClaimExists,
                    new StudentNumberVerificationStateDto(profile.Id, null, pendingDto),
                    pendingDto);
            }

            var nowUtc = UtcNow;
            var claim = new StudentNumberClaim
            {
                StudentProfileId = profile.Id,
                StudentProfile = profile,
                ClaimedStudentNumber = normalizedStudentNumber,
                Status = StudentNumberClaimStatus.Pending,
                SubmittedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc,
                CreatedBy = userId
            };
            EnsureNonRelationalRowVersion(claim);
            _context.StudentNumberClaims.Add(claim);
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "StudentNumberClaim.Submitted",
                EntityName = nameof(StudentNumberClaim),
                Details = "A student submitted an LMS identifier for administrator verification.",
                TimestampUtc = nowUtc
            });

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            var dto = ToClaimDto(claim, profile.User);
            return new StudentNumberClaimResultDto(
                StudentNumberClaimOutcome.Submitted,
                new StudentNumberVerificationStateDto(profile.Id, null, dto),
                dto);
        }
        catch (DbUpdateException exception) when (ContainsConstraint(exception, PendingClaimIndex))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.PendingClaimExists);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<StudentNumberClaimResultDto> ReviewAsync(
        int claimId,
        bool approve,
        string reviewerUserId,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var claim = await _context.StudentNumberClaims
                .Include(item => item.StudentProfile)
                .ThenInclude(profile => profile!.User)
                .SingleOrDefaultAsync(item => item.Id == claimId, cancellationToken);
            if (claim?.StudentProfile is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.NotFound);
            }

            if (!claim.RowVersion.SequenceEqual(expectedRowVersion))
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.ConcurrencyConflict);
            }
            if (claim.Status != StudentNumberClaimStatus.Pending)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                var reviewedDto = ToClaimDto(claim, claim.StudentProfile.User);
                return new StudentNumberClaimResultDto(
                    StudentNumberClaimOutcome.AlreadyReviewed,
                    new StudentNumberVerificationStateDto(
                        claim.StudentProfileId,
                        claim.StudentProfile.StudentNumber,
                        reviewedDto),
                    reviewedDto);
            }

            if (approve && !string.IsNullOrWhiteSpace(claim.StudentProfile.StudentNumber))
            {
                var state = await BuildStateAsync(claim.StudentProfile, cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(
                    StudentNumberClaimOutcome.AlreadyVerified,
                    state,
                    ToClaimDto(claim, claim.StudentProfile.User));
            }

            if (approve)
            {
                var claimedNumber = claim.ClaimedStudentNumber;
                var duplicateExists = await _context.StudentProfiles
                    .AsNoTracking()
                    .AnyAsync(
                        profile => profile.Id != claim.StudentProfileId &&
                                   profile.StudentNumber != null &&
                                   profile.StudentNumber.ToUpper() == claimedNumber,
                        cancellationToken);
                if (duplicateExists)
                {
                    var state = await BuildStateAsync(claim.StudentProfile, cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                    return new StudentNumberClaimResultDto(
                        StudentNumberClaimOutcome.DuplicateStudentNumber,
                        state,
                        ToClaimDto(claim, claim.StudentProfile.User));
                }
            }

            _context.Entry(claim).Property(item => item.RowVersion).OriginalValue = expectedRowVersion;
            var nowUtc = UtcNow;
            claim.Status = approve
                ? StudentNumberClaimStatus.Approved
                : StudentNumberClaimStatus.Rejected;
            claim.ReviewedAtUtc = nowUtc;
            claim.ReviewedByUserId = reviewerUserId;
            claim.ModifiedAtUtc = nowUtc;
            claim.ModifiedBy = reviewerUserId;
            if (approve)
            {
                claim.StudentProfile.StudentNumber = claim.ClaimedStudentNumber;
                claim.StudentProfile.ModifiedAtUtc = nowUtc;
                claim.StudentProfile.ModifiedBy = reviewerUserId;
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = reviewerUserId,
                Action = approve ? "StudentNumberClaim.Approved" : "StudentNumberClaim.Rejected",
                EntityName = nameof(StudentNumberClaim),
                EntityId = claim.Id.ToString(CultureInfo.InvariantCulture),
                Details = approve
                    ? "An administrator verified the student's LMS identifier."
                    : "An administrator rejected the student's LMS identifier claim.",
                TimestampUtc = nowUtc
            });

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            var dto = ToClaimDto(claim, claim.StudentProfile.User);
            return new StudentNumberClaimResultDto(
                approve ? StudentNumberClaimOutcome.Approved : StudentNumberClaimOutcome.Rejected,
                new StudentNumberVerificationStateDto(
                    claim.StudentProfileId,
                    claim.StudentProfile.StudentNumber,
                    dto),
                dto);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (ContainsConstraint(exception, VerifiedStudentNumberIndex))
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.DuplicateStudentNumber);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    public async Task<StudentNumberClaimResultDto> ResetAsync(
        int studentProfileId,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var profile = await _context.StudentProfiles
                .Include(item => item.User)
                .Include(item => item.RiskMonitoringConsent)
                .Include(item => item.StudentNumberClaims)
                .SingleOrDefaultAsync(item => item.Id == studentProfileId, cancellationToken);
            if (profile is null)
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.NotFound);
            }

            var nowUtc = UtcNow;
            foreach (var pendingClaim in profile.StudentNumberClaims.Where(
                         claim => claim.Status == StudentNumberClaimStatus.Pending))
            {
                pendingClaim.Status = StudentNumberClaimStatus.Rejected;
                pendingClaim.ReviewedAtUtc = nowUtc;
                pendingClaim.ReviewedByUserId = reviewerUserId;
                pendingClaim.ModifiedAtUtc = nowUtc;
                pendingClaim.ModifiedBy = reviewerUserId;
            }

            profile.StudentNumber = null;
            profile.ModifiedAtUtc = nowUtc;
            profile.ModifiedBy = reviewerUserId;

            var consent = profile.RiskMonitoringConsent;
            if (consent?.IsConsented == true)
            {
                consent.IsConsented = false;
                consent.PolicyVersion = RiskMonitoringPolicy.CurrentVersion;
                consent.WithdrawnAtUtc = nowUtc;
                consent.ModifiedAtUtc = nowUtc;
                consent.ModifiedBy = reviewerUserId;
                _context.RiskMonitoringConsentHistory.Add(new RiskMonitoringConsentHistory
                {
                    StudentProfileId = profile.Id,
                    IsConsented = false,
                    PolicyVersion = RiskMonitoringPolicy.CurrentVersion,
                    ChangedAtUtc = nowUtc,
                    ChangedByUserId = reviewerUserId
                });
            }

            await RemoveDerivedMonitoringDataAsync(profile.Id, cancellationToken);
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = reviewerUserId,
                Action = "StudentNumber.Reset",
                EntityName = nameof(StudentProfile),
                EntityId = profile.Id.ToString(CultureInfo.InvariantCulture),
                Details = "An administrator cleared the verified LMS identifier, disabled monitoring, and purged derived monitoring data.",
                TimestampUtc = nowUtc
            });

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            var latestClaim = profile.StudentNumberClaims
                .OrderByDescending(claim => claim.SubmittedAtUtc)
                .ThenByDescending(claim => claim.Id)
                .FirstOrDefault();
            var latestDto = latestClaim is null ? null : ToClaimDto(latestClaim, profile.User);
            return new StudentNumberClaimResultDto(
                StudentNumberClaimOutcome.Reset,
                new StudentNumberVerificationStateDto(profile.Id, null, latestDto),
                latestDto);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            return new StudentNumberClaimResultDto(StudentNumberClaimOutcome.ConcurrencyConflict);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task RemoveDerivedMonitoringDataAsync(
        int studentProfileId,
        CancellationToken cancellationToken)
    {
        var queueEntries = await _context.RiskQueueEntries
            .Where(entry => entry.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var scores = await _context.RiskScores
            .Where(score => score.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var snapshots = await _context.RiskFeatureSnapshots
            .Where(snapshot => snapshot.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);
        var activityLogs = await _context.ActivityLogs
            .Where(activity => activity.StudentProfileId == studentProfileId)
            .ToListAsync(cancellationToken);

        _context.RiskQueueEntries.RemoveRange(queueEntries);
        _context.RiskScores.RemoveRange(scores);
        _context.RiskFeatureSnapshots.RemoveRange(snapshots);
        _context.ActivityLogs.RemoveRange(activityLogs);
    }

    private async Task<StudentNumberVerificationStateDto> BuildStateAsync(
        StudentProfile profile,
        CancellationToken cancellationToken)
    {
        var latestClaim = await _context.StudentNumberClaims
            .AsNoTracking()
            .Where(claim => claim.StudentProfileId == profile.Id)
            .OrderByDescending(claim => claim.SubmittedAtUtc)
            .ThenByDescending(claim => claim.Id)
            .Select(claim => new ClaimProjection(
                claim.Id,
                claim.StudentProfileId,
                claim.ClaimedStudentNumber,
                claim.Status,
                claim.SubmittedAtUtc,
                claim.ReviewedAtUtc,
                claim.ReviewedByUserId,
                claim.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);
        return new StudentNumberVerificationStateDto(
            profile.Id,
            profile.StudentNumber,
            latestClaim is null
                ? null
                : ToClaimDto(latestClaim, profile.User?.FullName, profile.User?.Email));
    }

    private static StudentNumberVerificationStateDto ToState(
        ProfileProjection profile,
        ClaimProjection? latestClaim)
        => new(
            profile.Id,
            profile.StudentNumber,
            latestClaim is null
                ? null
                : ToClaimDto(latestClaim, profile.StudentName, profile.StudentEmail));

    private static StudentNumberClaimDto ToClaimDto(StudentNumberClaim claim, ApplicationUser? user)
        => new(
            claim.Id,
            claim.StudentProfileId,
            StudentName(user?.FullName),
            user?.Email,
            claim.ClaimedStudentNumber,
            claim.Status,
            claim.SubmittedAtUtc,
            claim.ReviewedAtUtc,
            claim.ReviewedByUserId,
            Convert.ToBase64String(claim.RowVersion ?? Array.Empty<byte>()));

    private static StudentNumberClaimDto ToClaimDto(
        ClaimProjection claim,
        string? studentName,
        string? studentEmail)
        => new(
            claim.Id,
            claim.StudentProfileId,
            StudentName(studentName),
            studentEmail,
            claim.ClaimedStudentNumber,
            claim.Status,
            claim.SubmittedAtUtc,
            claim.ReviewedAtUtc,
            claim.ReviewedByUserId,
            Convert.ToBase64String(claim.RowVersion ?? Array.Empty<byte>()));

    private static string StudentName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Student" : value;

    private void EnsureNonRelationalRowVersion(StudentNumberClaim claim)
    {
        if (!_context.Database.IsRelational() && claim.RowVersion.Length == 0)
            claim.RowVersion = Guid.NewGuid().ToByteArray();
    }

    private static bool ContainsConstraint(Exception exception, string constraintName)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed record ProfileProjection(
        int Id,
        string? StudentName,
        string? StudentEmail,
        string? StudentNumber);

    private sealed record ClaimProjection(
        int Id,
        int StudentProfileId,
        string ClaimedStudentNumber,
        StudentNumberClaimStatus Status,
        DateTime SubmittedAtUtc,
        DateTime? ReviewedAtUtc,
        string? ReviewedByUserId,
        byte[] RowVersion);
}
