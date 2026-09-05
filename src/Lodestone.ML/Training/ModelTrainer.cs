using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;

namespace Lodestone.ML.Training;

/// <summary>Trains the binary classification model and persists it to SavedModels.</summary>
public class ModelTrainer
{
    private readonly MLContext _mlContext;

    public ModelTrainer(MLContext mlContext) => _mlContext = mlContext;

    public ITransformer Train(IDataView trainingData, IEstimator<ITransformer> pipeline)
        => Train(trainingData, pipeline, ModelTrainingCandidate.V1FastTree);

    public ITransformer Train(
        IDataView trainingData,
        IEstimator<ITransformer> pipeline,
        ModelTrainingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(trainingData);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(candidate);

        IEstimator<ITransformer> trainer = candidate.Algorithm switch
        {
            ModelTrainingAlgorithm.FastTree => _mlContext.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    LabelColumnName = "Label",
                    FeatureColumnName = FeatureEngineering.FeaturesColumnName,
                    ExampleWeightColumnName = nameof(Models.StudentActivityObservation.ExampleWeight),
                    NumberOfTrees = candidate.Iterations,
                    NumberOfLeaves = candidate.NumberOfLeaves,
                    MinimumExampleCountPerLeaf = candidate.MinimumExampleCountPerLeaf,
                    LearningRate = candidate.LearningRate,
                    NumberOfThreads = 1
                }),
            ModelTrainingAlgorithm.LightGbm => _mlContext.BinaryClassification.Trainers.LightGbm(
                new LightGbmBinaryTrainer.Options
                {
                    LabelColumnName = "Label",
                    FeatureColumnName = FeatureEngineering.FeaturesColumnName,
                    ExampleWeightColumnName = nameof(Models.StudentActivityObservation.ExampleWeight),
                    NumberOfIterations = candidate.Iterations,
                    NumberOfLeaves = candidate.NumberOfLeaves,
                    MinimumExampleCountPerLeaf = candidate.MinimumExampleCountPerLeaf,
                    LearningRate = candidate.LearningRate,
                    NumberOfThreads = 1
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Algorithm, "Unsupported model algorithm.")
        };

        // Both the normalizer and classifier are fitted exclusively on the training partition.
        return pipeline.AppendCacheCheckpoint(_mlContext).Append(trainer).Fit(trainingData);
    }

    public void Save(ITransformer model, DataViewSchema schema, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _mlContext.Model.Save(model, schema, outputPath);
    }
}
