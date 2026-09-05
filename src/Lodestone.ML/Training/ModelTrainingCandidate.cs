using System.Globalization;

namespace Lodestone.ML.Training;

public enum ModelTrainingAlgorithm
{
    FastTree,
    LightGbm
}

/// <summary>
/// Fixed, reproducible candidate definition. Experiment v2 deliberately uses this bounded grid
/// rather than allowing command-line metric or hyperparameter overrides.
/// </summary>
public sealed record ModelTrainingCandidate(
    string Id,
    ModelTrainingAlgorithm Algorithm,
    int Iterations,
    int NumberOfLeaves,
    int MinimumExampleCountPerLeaf,
    double LearningRate)
{
    public static readonly ModelTrainingCandidate V1FastTree = new(
        "fasttree-v1-200-31-20-0.1",
        ModelTrainingAlgorithm.FastTree,
        200,
        31,
        20,
        .1);

    public static readonly IReadOnlyList<ModelTrainingCandidate> V2Candidates =
    [
        new("fasttree-200-31-10-0.05", ModelTrainingAlgorithm.FastTree, 200, 31, 10, .05),
        new("fasttree-300-31-20-0.1", ModelTrainingAlgorithm.FastTree, 300, 31, 20, .1),
        new("fasttree-400-63-10-0.05", ModelTrainingAlgorithm.FastTree, 400, 63, 10, .05),
        new("fasttree-400-63-20-0.1", ModelTrainingAlgorithm.FastTree, 400, 63, 20, .1),
        new("lightgbm-200-15-10-0.05", ModelTrainingAlgorithm.LightGbm, 200, 15, 10, .05),
        new("lightgbm-300-31-20-0.05", ModelTrainingAlgorithm.LightGbm, 300, 31, 20, .05),
        new("lightgbm-400-63-10-0.03", ModelTrainingAlgorithm.LightGbm, 400, 63, 10, .03),
        new("lightgbm-400-63-20-0.05", ModelTrainingAlgorithm.LightGbm, 400, 63, 20, .05)
    ];

    public IReadOnlyDictionary<string, string> ToReportValues()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["iterations"] = Iterations.ToString(CultureInfo.InvariantCulture),
            ["numberOfLeaves"] = NumberOfLeaves.ToString(CultureInfo.InvariantCulture),
            ["minimumExampleCountPerLeaf"] = MinimumExampleCountPerLeaf.ToString(CultureInfo.InvariantCulture),
            ["learningRate"] = LearningRate.ToString("0.################", CultureInfo.InvariantCulture),
            ["numberOfThreads"] = "1"
        };
}
