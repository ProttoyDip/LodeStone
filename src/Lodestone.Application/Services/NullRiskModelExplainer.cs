using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;

namespace Lodestone.Application.Services;

/// <summary>
/// Safe default used when no model is loaded. Returning null rather than throwing keeps the queue
/// rendering the score and the raw feature values it always showed; an unexplainable prediction is
/// a missing panel, never a broken page.
/// </summary>
public sealed class NullRiskModelExplainer : IRiskModelExplainer
{
    public bool IsAvailable => false;

    public RiskExplanation? Explain(RiskModelInput input, RiskExplanationBaseline baseline) => null;
}
