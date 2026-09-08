using System.Reflection;
using Lodestone.ML.Models;
using Microsoft.ML;

namespace Lodestone.ML.Training;

/// <summary>
/// Model-wide feature importance by permutation: shuffle one input across the validation rows,
/// re-score, and record how far ROC AUC falls. Averaged over a few shuffles so a single unlucky
/// permutation cannot dominate.
/// </summary>
/// <remarks>
/// Implemented by hand on the already-loaded observation rows rather than through ML.NET's PFI
/// extension so it does not depend on the trained predictor's concrete type or on slot names
/// surviving the feature pipeline -- the same reason the runtime explainer treats the model as a
/// black box. It reports what the model relies on; it says nothing about any one student.
/// </remarks>
public sealed class PermutationImportanceCalculator
{
    private readonly MLContext _mlContext;
    private readonly ModelEvaluator _evaluator;

    public PermutationImportanceCalculator(MLContext mlContext, ModelEvaluator evaluator)
    {
        _mlContext = mlContext;
        _evaluator = evaluator;
    }

    public IReadOnlyList<FeatureImportanceEntry> Compute(
        ITransformer model,
        IReadOnlyList<StudentActivityObservation> rows,
        IReadOnlyList<string> featureNames,
        int seed,
        int permutationCount = 3)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(featureNames);
        if (permutationCount < 1) throw new ArgumentOutOfRangeException(nameof(permutationCount));
        if (rows.Count < 2 || featureNames.Count == 0) return Array.Empty<FeatureImportanceEntry>();

        var baselineAuc = Auc(model, rows);
        var properties = featureNames.ToDictionary(
            name => name,
            name => typeof(StudentActivityFeatures).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"Feature '{name}' is not a property of {nameof(StudentActivityFeatures)}."),
            StringComparer.Ordinal);

        var drops = new Dictionary<string, double>(StringComparer.Ordinal);
        var random = new Random(seed);
        foreach (var name in featureNames)
        {
            var property = properties[name];
            var total = 0d;
            for (var repeat = 0; repeat < permutationCount; repeat++)
            {
                var permuted = Clone(rows);
                var values = permuted.Select(row => (float)property.GetValue(row)!).ToArray();
                Shuffle(values, random);
                for (var index = 0; index < permuted.Count; index++)
                    property.SetValue(permuted[index], values[index]);
                total += baselineAuc - Auc(model, permuted);
            }
            drops[name] = total / permutationCount;
        }

        // Only positive drops carry signal; a shuffle that improves AUC is noise, not "negative importance".
        var positiveTotal = drops.Values.Where(drop => drop > 0).Sum();
        return drops
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select((pair, index) => new FeatureImportanceEntry
            {
                FeatureName = pair.Key,
                Rank = index + 1,
                MeanAucDrop = Math.Round(pair.Value, 6),
                Share = positiveTotal <= 0 || pair.Value <= 0 ? 0d : Math.Round(pair.Value / positiveTotal, 6),
                PermutationCount = permutationCount
            })
            .ToArray();
    }

    private double Auc(ITransformer model, IReadOnlyList<StudentActivityObservation> rows)
        => _evaluator.Evaluate(model, _mlContext.Data.LoadFromEnumerable(rows)).AreaUnderRocCurve;

    private static List<StudentActivityObservation> Clone(IReadOnlyList<StudentActivityObservation> rows)
    {
        var featureProperties = typeof(StudentActivityFeatures)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .ToArray();

        var clones = new List<StudentActivityObservation>(rows.Count);
        foreach (var row in rows)
        {
            var clone = new StudentActivityObservation
            {
                IsAtRisk = row.IsAtRisk,
                ExampleWeight = row.ExampleWeight,
                StudentGroupKey = row.StudentGroupKey,
                EnrollmentKey = row.EnrollmentKey,
                ObservationDay = row.ObservationDay,
                CoursePresentationKey = row.CoursePresentationKey,
                WithdrawalDay = row.WithdrawalDay
            };
            foreach (var property in featureProperties)
                property.SetValue(clone, property.GetValue(row));
            clones.Add(clone);
        }
        return clones;
    }

    private static void Shuffle(float[] values, Random random)
    {
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
    }
}
