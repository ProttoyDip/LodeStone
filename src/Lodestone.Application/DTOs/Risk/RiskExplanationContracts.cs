namespace Lodestone.Application.DTOs.Risk;

/// <summary>Which way a factor pushed the score.</summary>
public enum RiskFactorDirection
{
    /// <summary>Removing this factor lowers the score: it is part of why the student was flagged.</summary>
    Raising,

    /// <summary>Removing this factor raises the score: it is working in the student's favour.</summary>
    Protective
}

/// <summary>
/// One feature's measured influence on a single prediction.
/// </summary>
/// <param name="FeatureName">The model's own feature name, unchanged, so a claim can be traced.</param>
/// <param name="DisplayName">A counselor-readable phrase for the same quantity.</param>
/// <param name="Value">This student's value for the feature.</param>
/// <param name="BaselineValue">The reference value the contribution was measured against.</param>
/// <param name="Contribution">
/// Change in predicted probability attributable to this feature: the score as measured minus the
/// score when this feature alone is replaced by <paramref name="BaselineValue"/>. Positive means
/// the feature raised the score.
/// </param>
public sealed record RiskFactor(
    string FeatureName,
    string DisplayName,
    float Value,
    float BaselineValue,
    double Contribution)
{
    public RiskFactorDirection Direction =>
        Contribution >= 0 ? RiskFactorDirection.Raising : RiskFactorDirection.Protective;

    /// <summary>Size of the influence regardless of direction, for ranking.</summary>
    public double Magnitude => Math.Abs(Contribution);
}

/// <summary>
/// Why the model scored one student as it did.
/// </summary>
/// <remarks>
/// <para>
/// Contributions are measured, not asserted: each is the actual movement in the model's output
/// when one feature is set to a reference value and everything else is held still. They describe
/// the model's decision surface, and nothing more.
/// </para>
/// <para>
/// In particular they are <b>not causal</b>. A feature that raised the score is not a lever that
/// changes whether a student withdraws; both the behaviour and the outcome sit downstream of
/// whatever is actually happening in that student's life. <see cref="BoundaryNote"/> is phrased to
/// respect this: it says where the model's own boundary lies, never what anyone should do.
/// </para>
/// </remarks>
public sealed record RiskExplanation(
    double Probability,
    string ModelVersion,
    string FeatureSchemaVersion,
    IReadOnlyList<RiskFactor> Factors,
    string BaselineDescription,
    string? BoundaryNote)
{
    /// <summary>Factors that pushed the score up, largest first.</summary>
    public IReadOnlyList<RiskFactor> RaisingFactors => Factors
        .Where(factor => factor.Direction == RiskFactorDirection.Raising && factor.Magnitude > 0)
        .OrderByDescending(factor => factor.Magnitude)
        .ToArray();

    /// <summary>Factors working in the student's favour, largest first.</summary>
    public IReadOnlyList<RiskFactor> ProtectiveFactors => Factors
        .Where(factor => factor.Direction == RiskFactorDirection.Protective && factor.Magnitude > 0)
        .OrderByDescending(factor => factor.Magnitude)
        .ToArray();
}
