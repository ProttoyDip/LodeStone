using System.Globalization;
using System.Text;
using Lodestone.Application.DTOs.Risk;
using Lodestone.Application.Interfaces;
using Lodestone.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lodestone.Infrastructure.Services;

public sealed class DemoSnapshotSampleService : IDemoSnapshotSampleService
{
    // Two anchor profiles for withdrawal-28d-v3, in schema order. Blending from the healthy profile
    // toward the disengaged one spreads students across the score range; the disengaged profile is a
    // sharp drop in clicks with late or missing assessments, which the model scores as high risk.
    private static readonly float[] Healthy =
        { 0.8f, 0.8f, 0f, 12f, 12f, 0f, 1f, 0.6f, 0.95f, 0.05f, 0.85f, 0.8f, 0f, 1f, 0.3f, 0f, 0f };

    private static readonly float[] Disengaged =
        { 0.518f, 0.162f, 0.356f, 0f, 11.972f, -1f, 2.08f, 0.108f, 1f, 1f, 0.167f, 0.266f, -0.962f, 1.785f, 0.448f, 0.288f, 2.16f };

    private readonly ApplicationDbContext _context;

    public DemoSnapshotSampleService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string?> BuildCsvAsync(
        string featureSchemaVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!RiskFeatureSchemas.TryGet(featureSchemaVersion, out var schema) ||
            !string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV3, StringComparison.Ordinal) ||
            schema.FeatureNames.Count != Healthy.Length)
        {
            return null;
        }

        var studentNumbers = await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.StudentNumber != null && profile.StudentNumber != "" &&
                              profile.User != null && profile.User.IsActive &&
                              profile.RiskMonitoringConsent != null && profile.RiskMonitoringConsent.IsConsented)
            .Select(profile => profile.StudentNumber!)
            .OrderBy(number => number)
            .ToListAsync(cancellationToken);

        var courseKey = "DEMO-" + nowUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var windowEnd = nowUtc.Date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var csv = new StringBuilder();
        csv.Append("StudentNumber,CourseKey,WindowEndUtc,ObservedDays,FeatureSchemaVersion,")
            .Append(string.Join(",", schema.FeatureNames)).Append("\r\n");

        foreach (var studentNumber in studentNumbers)
        {
            var blend = Blend(SeverityFor(studentNumber));
            csv.Append(Escape(studentNumber)).Append(',')
                .Append(courseKey).Append(',')
                .Append(windowEnd).Append(',')
                .Append(schema.ObservedDays.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(schema.Version).Append(',')
                .Append(string.Join(",", blend.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture))))
                .Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>
    /// Stable 0..1 severity per student: about one in five is clearly at risk, one in six borderline,
    /// and the rest spread across the low range. The same student always gets the same profile.
    /// </summary>
    private static double SeverityFor(string studentNumber)
    {
        uint hash = 2166136261;
        foreach (var character in studentNumber)
            hash = (hash ^ character) * 16777619;

        var bucket = (int)(hash % 100);
        return bucket switch
        {
            < 20 => 0.95 + (bucket % 5) * 0.0125,
            < 35 => 0.9,
            _ => (bucket - 35) / 65.0 * 0.75
        };
    }

    private static float[] Blend(double severity)
    {
        var values = new float[Healthy.Length];
        for (var index = 0; index < values.Length; index++)
            values[index] = (float)(Healthy[index] * (1 - severity) + Disengaged[index] * severity);

        // The two trend features are derived, so keep them consistent with the blended rates.
        values[2] = Math.Clamp(values[0] - values[1], -1f, 1f);
        values[5] = Math.Clamp((values[3] - values[4]) / values[4], -1f, 1f);
        return values;
    }

    private static string Escape(string value)
        => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
