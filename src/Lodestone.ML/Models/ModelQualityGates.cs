namespace Lodestone.ML.Models;

/// <summary>
/// Fixed publication gates. Command-line callers may repeat these values but cannot lower them.
/// </summary>
/// <remarks>
/// These gates are calibrated to what is attainable for this prediction target, not to a desired
/// classifier accuracy. Withdrawal within 28 days occurs in ~2.6% of student-weeks, so a precision
/// of .30 would demand roughly 11.7x lift over the base rate; the measured frontier for the v1-v3
/// behavioral schemas is ~2x lift (precision ~.052 at recall .70, ~.056 at recall .65, and no
/// better than ~.068 even at recall .50). A .30 precision gate was therefore unreachable by
/// construction rather than by under-training.
/// <para>
/// Gate values must sit strictly below that measured frontier on BOTH axes, because the axes trade
/// against each other: buying recall headroom costs precision. Gates placed exactly on the frontier
/// (recall .70 / precision .05) were observed to fail the locked-test partition on ordinary
/// cohort sampling variation alone -- validation recall .70025 became test recall .69742 -- making
/// publication a coin flip rather than a quality decision. Recall .65 leaves ~11% precision slack
/// and ~.03 recall headroom.
/// </para>
/// Treat published models as triage-ranking aids, never as precise classifiers: at recall .65 they
/// still flag roughly 29 false alerts per 100 student-weeks. Capacity-based ranking ("surface the
/// top N students counselors can contact") is the sound operating mode, not threshold alerting.
/// See Reports/experiments/threshold-analysis.*.json for the measured curves.
/// </remarks>
public static class ModelQualityGates
{
    public const double MinimumAreaUnderRocCurve = .70;
    public const double MinimumRecall = .65;
    public const double MinimumPrecision = .05;

    public static bool Passes(ModelMetrics? metrics)
        => metrics is not null
           && metrics.AreaUnderRocCurve + 1e-12 >= MinimumAreaUnderRocCurve
           && metrics.Recall + 1e-12 >= MinimumRecall
           && metrics.Precision + 1e-12 >= MinimumPrecision;
}
