using Lodestone.ML.Models;

namespace Lodestone.ML.Training;

public sealed record TrainingResult(
    RiskModelMetadata Metadata,
    TrainingReport Report,
    string ModelPath,
    string MetadataPath,
    string ReportPath);

public sealed class ModelQualityGateException : InvalidOperationException
{
    public ModelQualityGateException(string message) : base(message) { }

    public ModelQualityGateException(
        string message,
        TrainingReport report,
        string failureReportPath,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Report = report;
        FailureReportPath = failureReportPath;
    }

    public TrainingReport? Report { get; }
    public string? FailureReportPath { get; }
}
