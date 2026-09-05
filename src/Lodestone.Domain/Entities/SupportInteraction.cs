using Lodestone.Domain.Common;
using Lodestone.Domain.Enums;

namespace Lodestone.Domain.Entities;

public class SupportInteraction : AuditableEntity
{
    public int SupportRequestId { get; set; }
    public SupportRequest? SupportRequest { get; set; }

    public string? VolunteerUserId { get; set; }
    public string? StudentUserId { get; set; }

    public SupportInteractionType Type { get; set; } = SupportInteractionType.Message;
    public string Message { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool EscalatedToCounselor { get; set; }
}
