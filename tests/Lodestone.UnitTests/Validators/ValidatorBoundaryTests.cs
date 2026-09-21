using FluentAssertions;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.DTOs.Forum;
using Lodestone.Application.DTOs.Journal;
using Lodestone.Application.Validators;
using Xunit;

namespace Lodestone.UnitTests.Validators;

/// <summary>
/// Boundary and invalid-input coverage for every request validator. The per-validator test classes
/// cover the typical accepted shape; this class pins the exact edge of each rule, because an
/// off-by-one in a length or range rule is invisible until a real submission lands on it.
/// </summary>
public class ValidatorBoundaryTests
{
    private readonly ForumPostValidator _post = new();
    private readonly ForumCommentValidator _comment = new();
    private readonly JournalEntryValidator _journal = new();
    private readonly BookingRequestValidator _booking = new();

    // ---------- Forum post ----------

    [Fact]
    public void ForumPost_AcceptsTitleAndBodyExactlyAtTheirLimits()
        => _post.Validate(new CreateForumPostDto(1, new string('t', 200), new string('b', 5000)))
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData(201, 100, "Title")]
    [InlineData(100, 5001, "Body")]
    public void ForumPost_RejectsOneCharacterPastTheLimit(int titleLength, int bodyLength, string property)
    {
        var result = _post.Validate(new CreateForumPostDto(1, new string('t', titleLength), new string('b', bodyLength)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == property);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t\n")]
    [InlineData(null)]
    public void ForumPost_RejectsATitleThatIsBlankOrAbsent(string? title)
        => _post.Validate(new CreateForumPostDto(1, title!, "A body.")).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ForumPost_RejectsANonPositiveCategory(int categoryId)
    {
        var result = _post.Validate(new CreateForumPostDto(categoryId, "Title", "Body"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateForumPostDto.CategoryId));
    }

    // ---------- Forum comment ----------

    [Fact]
    public void ForumComment_AcceptsABodyExactlyAtTheLimit()
        => _comment.Validate(new CreateForumCommentDto(1, new string('c', 2000))).IsValid.Should().BeTrue();

    [Fact]
    public void ForumComment_RejectsABodyOneCharacterPastTheLimit()
    {
        var result = _comment.Validate(new CreateForumCommentDto(1, new string('c', 2001)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateForumCommentDto.Body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ForumComment_RejectsAnEmptyBody(string? body)
        => _comment.Validate(new CreateForumCommentDto(1, body!)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ForumComment_RejectsANonPositivePostReference(int postId)
    {
        var result = _comment.Validate(new CreateForumCommentDto(postId, "A reply."));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateForumCommentDto.PostId));
    }

    // ---------- Journal entry ----------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Journal_AcceptsBothEndsOfTheMoodScale(int rating)
        => _journal.Validate(new CreateJournalEntryDto(rating, null)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Journal_RejectsRatingsOutsideTheScale(int rating)
        => _journal.Validate(new CreateJournalEntryDto(rating, null)).IsValid.Should().BeFalse();

    [Fact]
    public void Journal_AcceptsANoteExactlyAtTheLimit()
        => _journal.Validate(new CreateJournalEntryDto(3, new string('n', 2000))).IsValid.Should().BeTrue();

    [Fact]
    public void Journal_AcceptsAnAbsentNoteBecauseTheNoteIsOptional()
        => _journal.Validate(new CreateJournalEntryDto(3, null)).IsValid.Should().BeTrue();

    // ---------- Booking request ----------

    [Fact]
    public void Booking_AcceptsNotesExactlyAtTheLimit()
        => _booking.Validate(new CreateBookingDto(1, new string('x', 1000))).IsValid.Should().BeTrue();

    [Fact]
    public void Booking_AcceptsAnAbsentNoteBecauseContextIsOptional()
        => _booking.Validate(new CreateBookingDto(1, null)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Booking_RejectsANonPositiveSlot(int slotId)
    {
        var result = _booking.Validate(new CreateBookingDto(slotId, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateBookingDto.AvailabilitySlotId));
    }
}
