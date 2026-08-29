using Lodestone.Application.Interfaces;
using Lodestone.ML.Models;
using Lodestone.ML.Prediction;
using Lodestone.ML.Training;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;

namespace Lodestone.ML;

/// <summary>Registers ML.NET context, training components and the prediction service.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddMachineLearning(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var enabled = bool.TryParse(configuration["MachineLearning:Enabled"], out var configuredEnabled)
            && configuredEnabled;
        var modelPath = ResolvePath(
            configuration["MachineLearning:ModelPath"],
            contentRootPath,
            Path.Combine("App_Data", "ml", "risk-model.zip"));
        var metadataPath = ResolvePath(
            configuration["MachineLearning:MetadataPath"],
            contentRootPath,
            Path.ChangeExtension(Path.GetRelativePath(contentRootPath, modelPath), ".metadata.json"));

        return Register(services, enabled, modelPath, metadataPath);
    }

    /// <summary>Compatibility overload for non-hosted callers; a supplied path means enabled.</summary>
    public static IServiceCollection AddMachineLearning(this IServiceCollection services, string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        var resolvedModelPath = Path.GetFullPath(modelPath);
        return Register(
            services,
            enabled: true,
            resolvedModelPath,
            Path.ChangeExtension(resolvedModelPath, ".metadata.json"));
    }

    private static IServiceCollection Register(
        IServiceCollection services,
        bool enabled,
        string modelPath,
        string metadataPath)
    {
        var mlContext = new MLContext(seed: 42);
        var loadResult = enabled
            ? LoadedRiskModelPredictor.TryLoad(mlContext, modelPath, metadataPath)
            : new RiskModelLoadResult(
                new UnavailableRiskModelPredictor("Machine learning is disabled by configuration."),
                RiskModelStatus.Disabled());

        services.AddSingleton(mlContext);

        services.AddScoped<OuladDataLoader>();
        services.AddScoped<FeatureEngineering>();
        services.AddScoped<ModelTrainer>();
        services.AddScoped<ModelEvaluator>();
        services.AddScoped<TrainingPipeline>();

        services.AddSingleton(loadResult.Status);
        services.AddSingleton<IRiskModelPredictor>(loadResult.Predictor);
        services.AddSingleton<IRiskModelStatusProvider>(new RiskModelStatusProvider(loadResult.Status));

        return services;
    }

    private static string ResolvePath(string? configuredPath, string contentRootPath, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
        return Path.GetFullPath(Path.IsPathRooted(value)
            ? value
            : Path.Combine(contentRootPath, value));
    }
}
