using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

namespace Lodestone.ML.Training;

/// <summary>Trains the binary classification model and persists it to SavedModels.</summary>
public class ModelTrainer
{
    private readonly MLContext _mlContext;

    public ModelTrainer(MLContext mlContext) => _mlContext = mlContext;

    public ITransformer Train(IDataView trainingData, IEstimator<ITransformer> pipeline)
    {
        ArgumentNullException.ThrowIfNull(trainingData);
        ArgumentNullException.ThrowIfNull(pipeline);

        var trainer = _mlContext.BinaryClassification.Trainers.FastTree(
            new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = "Label",
                FeatureColumnName = FeatureEngineering.FeaturesColumnName,
                ExampleWeightColumnName = nameof(Models.StudentActivityObservation.ExampleWeight),
                NumberOfTrees = 200,
                NumberOfLeaves = 31,
                MinimumExampleCountPerLeaf = 20,
                LearningRate = 0.1,
                NumberOfThreads = 1,
            });

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
