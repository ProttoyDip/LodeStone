using Lodestone.Domain.Common;

namespace Lodestone.Domain.Entities;

public class CounselorAvailabilitySlot : AuditableEntity
{
    public int CounselorProfileId { get; set; }
    public CounselorProfile? CounselorProfile { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsBooked { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
