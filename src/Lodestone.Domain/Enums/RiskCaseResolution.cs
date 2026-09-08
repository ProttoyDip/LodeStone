namespace Lodestone.Domain.Enums;

/// <summary>
/// Why a counselor closed a behavioural-risk case. Recorded so the report can show what the
/// queue actually led to, not merely that it was emptied.
/// </summary>
public enum RiskCaseResolution
{
    /// <summary>Legacy rows resolved before reasons were captured.</summary>
    Unspecified = 0,
    /// <summary>Counselor reached the student and a conversation or appointment took place.</summary>
    Contacted = 1,
    /// <summary>Counselor reviewed the case and found no cause for concern.</summary>
    NoConcern = 2,
    /// <summary>Case handed to another service, department, or external provider.</summary>
    Referred = 3,
    /// <summary>Student was offered support and declined it.</summary>
    StudentDeclined = 4,
    /// <summary>Counselor tried but could not reach the student.</summary>
    Unreachable = 5
}
