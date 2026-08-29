using Lodestone.Application.DTOs.Risk;

namespace Lodestone.Application.Interfaces;

/// <summary>Application-owned boundary implemented by the ML plug-in.</summary>
public interface IRiskModelPredictor
{
    RiskModelDescriptor Descriptor { get; }
    RiskModelPrediction Predict(RiskModelInput input);
}
