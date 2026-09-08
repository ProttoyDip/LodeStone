namespace Lodestone.ML.Evaluation;

/// <summary>
/// Confusion-matrix and ranking statistics for one group of scored rows.
/// </summary>
/// <remarks>
/// Separated from <see cref="FairnessAuditor"/> so the arithmetic behind a fairness claim can be
/// tested against hand-checked cases. A subgroup metric that nobody has verified is worse than no
/// subgroup metric, because it will be quoted.
/// </remarks>
public static class FairnessMetrics
{
    /// <summary>A scored row reduced to what any of these statistics need.</summary>
    public readonly record struct Outcome(bool Label, float Probability);

    /// <summary>
    /// Applies <paramref name="threshold"/> as the decision rule and returns the resulting rates.
    /// A row is flagged when its probability is greater than or equal to the threshold, matching
    /// ModelEvaluator so audit numbers and headline numbers cannot disagree by convention alone.
    /// </summary>
    public static FairnessGroupMetrics Compute(string group, IReadOnlyList<Outcome> rows, double threshold)
    {
        ArgumentNullException.ThrowIfNull(rows);

        int truePositive = 0, falsePositive = 0, trueNegative = 0, falseNegative = 0;
        foreach (var row in rows)
        {
            var predicted = row.Probability >= threshold;
            if (predicted && row.Label) truePositive++;
            else if (predicted) falsePositive++;
            else if (row.Label) falseNegative++;
            else trueNegative++;
        }

        var positives = truePositive + falseNegative;
        var negatives = falsePositive + trueNegative;
        var flagged = truePositive + falsePositive;

        return new FairnessGroupMetrics
        {
            Group = group,
            RowCount = rows.Count,
            PositiveCount = positives,
            BaseRate = rows.Count == 0 ? 0 : positives / (double)rows.Count,
            SelectionRate = rows.Count == 0 ? 0 : flagged / (double)rows.Count,
            Recall = positives == 0 ? 0 : truePositive / (double)positives,
            Precision = flagged == 0 ? 0 : truePositive / (double)flagged,
            FalsePositiveRate = negatives == 0 ? 0 : falsePositive / (double)negatives,
            FalseNegativeRate = positives == 0 ? 0 : falseNegative / (double)positives,
            Accuracy = rows.Count == 0 ? 0 : (truePositive + trueNegative) / (double)rows.Count,
            AreaUnderRocCurve = AreaUnderRocCurve(rows),
            TruePositive = truePositive,
            FalsePositive = falsePositive,
            TrueNegative = trueNegative,
            FalseNegative = falseNegative
        };
    }

    /// <summary>
    /// Rank-based AUC (the Mann-Whitney statistic), computed directly so a group can be scored
    /// without rebuilding an IDataView per group. Tied scores share their averaged rank, which is
    /// what makes a model that gives every row the same probability score 0.5 rather than 1.0.
    /// Returns null when a group contains only one class, where AUC is undefined.
    /// </summary>
    public static double? AreaUnderRocCurve(IReadOnlyList<Outcome> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var positives = 0;
        foreach (var row in rows)
        {
            if (row.Label) positives++;
        }

        var negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0) return null;

        var ordered = rows.OrderBy(row => row.Probability).ToArray();
        var positiveRankSum = 0d;
        var position = 0;
        while (position < ordered.Length)
        {
            var end = position;
            while (end + 1 < ordered.Length && ordered[end + 1].Probability.Equals(ordered[position].Probability))
                end++;

            // Ranks are 1-based; a tied block shares the average of the ranks it spans.
            var averageRank = (position + end + 2) / 2d;
            for (var index = position; index <= end; index++)
            {
                if (ordered[index].Label) positiveRankSum += averageRank;
            }

            position = end + 1;
        }

        return (positiveRankSum - positives * (positives + 1) / 2d) / ((double)positives * negatives);
    }

    /// <summary>Largest difference in one metric across groups; null when fewer than two groups are reported.</summary>
    public static double? Gap(IReadOnlyList<FairnessGroupMetrics> groups, Func<FairnessGroupMetrics, double> select)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Count < 2 ? null : groups.Max(select) - groups.Min(select);
    }

    /// <summary>Lowest value divided by the highest; null when fewer than two groups or the highest is zero.</summary>
    public static double? Ratio(IReadOnlyList<FairnessGroupMetrics> groups, Func<FairnessGroupMetrics, double> select)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count < 2) return null;
        var highest = groups.Max(select);
        return highest <= 0 ? null : groups.Min(select) / highest;
    }
}
