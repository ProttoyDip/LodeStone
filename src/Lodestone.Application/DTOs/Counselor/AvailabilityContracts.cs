namespace Lodestone.Application.DTOs.Counselor;

public record PublishAvailabilitySlotDto(DateTime StartUtc, DateTime EndUtc);

public record CounselorAvailabilityPageDto(
    string CounselorName,
    IReadOnlyList<AvailabilitySlotDto> Slots);

public enum AvailabilityRemovalResult
{
    Removed,
    NotFound,
    Booked
}
