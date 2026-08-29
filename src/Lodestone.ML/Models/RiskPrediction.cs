using Microsoft.ML.Data;

namespace Lodestone.ML.Models;

/// <summary>Raw ML.NET prediction output for a single student.</summary>
public class RiskPrediction
{
    [ColumnName("PredictedLabel")]
    public bool IsAtRisk { get; set; }

    public float Probability { get; set; }
    public float Score { get; set; }

    [NoColumn]
    public string ModelVersion { get; set; } = string.Empty;

    [NoColumn]
    public float DecisionThreshold { get; set; } = 0.5f;
}
