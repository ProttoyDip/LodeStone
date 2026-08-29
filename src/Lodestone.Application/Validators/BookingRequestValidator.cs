using FluentValidation;
using Lodestone.Application.DTOs.Booking;

namespace Lodestone.Application.Validators;

public class BookingRequestValidator : AbstractValidator<CreateBookingDto>
{
    public BookingRequestValidator()
    {
        RuleFor(x => x.AvailabilitySlotId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
