namespace Lodestone.Application.Exceptions;

public sealed class BookingSlotUnavailableException : Exception
{
    public BookingSlotUnavailableException()
        : base("That appointment time is no longer available.") { }
}
