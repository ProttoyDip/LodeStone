namespace Lodestone.ML.Models;

/// <summary>Evaluation metrics captured after training.</summary>
public class ModelMetrics
{
    public double Accuracy { get; set; }
    public double AreaUnderRocCurve { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double DecisionThreshold { get; set; }
    public int RowCount { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public int TruePositive { get; set; }
    public int FalsePositive { get; set; }
    public int TrueNegative { get; set; }
    public int FalseNegative { get; set; }
    public string? ModelVersion { get; set; }
}
