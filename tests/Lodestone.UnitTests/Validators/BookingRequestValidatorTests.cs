using FluentAssertions;
using Lodestone.Application.DTOs.Booking;
using Lodestone.Application.Validators;
using Xunit;

namespace Lodestone.UnitTests.Validators;

public class BookingRequestValidatorTests
{
    [Fact]
    public void Validate_RejectsMissingSlotAndOversizedNotes()
    {
        var result = new BookingRequestValidator().Validate(new CreateBookingDto(0, new string('x', 1001)));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(new[] { "AvailabilitySlotId", "Notes" });
    }

    [Fact]
    public void Validate_AcceptsPublishedSlotSelection()
        => new BookingRequestValidator().Validate(new CreateBookingDto(12, "Optional context")).IsValid.Should().BeTrue();
}
