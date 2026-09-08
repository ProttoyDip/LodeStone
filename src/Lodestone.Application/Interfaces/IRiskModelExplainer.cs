using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

/// <summary>
/// Application-owned boundary for explaining a single risk prediction.
/// </summary>
/// <remarks>
/// Explanation is deliberately a read-only, on-demand computation. Nothing it produces is stored:
/// "why we believe this student may withdraw" is a stronger inference than the score itself, and
/// persisting it would create a new class of sensitive record needing its own consent basis,
/// retention rule and access control. Recomputing costs one extra model call per feature and is
/// only ever done for a student a counselor is already authorized to see.
/// </remarks>
public interface IRiskModelExplainer
{
    /// <summary>True when a model is loaded and able to explain.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Measures each feature's influence on this prediction against <paramref name="baseline"/>.
    /// Returns null when no model is loaded, so callers render the score alone rather than failing.
    /// </summary>
    RiskExplanation? Explain(RiskModelInput input, RiskExplanationBaseline baseline);
}
