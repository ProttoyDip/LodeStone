using System.Globalization;
using Lodestone.ML.Training;

namespace Lodestone.ML.Evaluation;

/// <summary>
/// The protected attributes of one OULAD enrollment, read from studentInfo.csv.
/// </summary>
/// <remarks>
/// These values exist only inside the offline audit. They are never loaded by
/// <see cref="OuladDataLoader"/>, never become features, and are never written to the application
/// database -- Lodestone's own <c>StudentProfile</c> has no demographic columns at all. That is a
/// deliberate privacy choice with a cost this audit exists to expose: a deployment that records no
/// protected attributes cannot measure its own bias, so the measurement has to happen here, on the
/// research dataset, before the model is trusted with anyone.
/// </remarks>
public sealed record ProtectedAttributes(
    string Gender,
    string AgeBand,
    string DeprivationBand,
    string Disability,
    string HighestEducation,
    string Region)
{
    /// <summary>Attribute names in the order they are reported.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["gender", "age_band", "imd_band", "disability", "highest_education", "region"];

    public string this[string attribute] => attribute switch
    {
        "gender" => Gender,
        "age_band" => AgeBand,
        "imd_band" => DeprivationBand,
        "disability" => Disability,
        "highest_education" => HighestEducation,
        "region" => Region,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Unknown protected attribute.")
    };
}

/// <summary>Loads protected attributes for audit use, keyed by module/presentation/student.</summary>
public static class ProtectedAttributeLoader
{
    /// <summary>Shown for a value the dataset does not record, so the gap is visible rather than dropped.</summary>
    public const string NotRecorded = "(not recorded)";

    /// <summary>
    /// Reads studentInfo.csv into a lookup keyed exactly as
    /// <c>StudentActivityObservation.EnrollmentKey</c> formats it: "module/presentation/studentId".
    /// </summary>
    public static IReadOnlyDictionary<string, ProtectedAttributes> Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.Combine(dataDirectory, "studentInfo.csv");
        if (!File.Exists(path))
            throw new FileNotFoundException($"studentInfo.csv was not found in '{dataDirectory}'.", path);

        var result = new Dictionary<string, ProtectedAttributes>(StringComparer.Ordinal);
        Rfc4180CsvReader.Read(
            path,
            [
                "code_module", "code_presentation", "id_student",
                "gender", "region", "highest_education", "imd_band", "age_band", "disability"
            ],
            row =>
            {
                var module = row["code_module"].Trim();
                var presentation = row["code_presentation"].Trim();
                var student = row["id_student"].Trim();
                if (module.Length == 0 || presentation.Length == 0 || student.Length == 0)
                    return;
                if (!int.TryParse(student, NumberStyles.None, CultureInfo.InvariantCulture, out var studentId))
                    return;

                var key = $"{module}/{presentation}/{studentId.ToString(CultureInfo.InvariantCulture)}";
                result[key] = new ProtectedAttributes(
                    Normalize(row["gender"]),
                    Normalize(row["age_band"]),
                    NormalizeDeprivationBand(row["imd_band"]),
                    Normalize(row["disability"]),
                    Normalize(row["highest_education"]),
                    Normalize(row["region"]));
            });

        return result;
    }

    private static string Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed == "?" ? NotRecorded : trimmed;
    }

    /// <summary>
    /// OULAD writes one deprivation band as "10-20" while every other band carries a percent sign.
    /// Left alone it would report as a separate category from its own siblings, so it is repaired
    /// here rather than in the shared loader, which never reads this column.
    /// </summary>
    private static string NormalizeDeprivationBand(string? value)
    {
        var normalized = Normalize(value);
        if (normalized == NotRecorded) return normalized;
        return normalized.EndsWith('%') ? normalized : normalized + "%";
    }
}
