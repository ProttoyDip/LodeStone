using System.Globalization;
using Lodestone.Application.DTOs.Risk;
using Lodestone.ML.Models;
using Microsoft.ML;

namespace Lodestone.ML.Training;

/// <summary>
/// Joins the seven canonical OULAD tables and produces leakage-safe, rolling behavioral
/// observations. Raw demographics, scores and text are intentionally excluded.
/// </summary>
public sealed class OuladDataLoader
{
    public const int ObservationWindowDays = 28;
    public const int PredictionWindowDays = 28;
    public const int ObservationStrideDays = 7;

    private static readonly string[] RequiredTables =
    [
        "courses.csv",
        "assessments.csv",
        "vle.csv",
        "studentAssessment.csv",
        "studentInfo.csv",
        "studentRegistration.csv",
        "studentVle.csv"
    ];

    private readonly MLContext _mlContext;

    public OuladDataLoader(MLContext mlContext) => _mlContext = mlContext;

    public IDataView Load(string dataDirectory)
        => _mlContext.Data.LoadFromEnumerable(LoadObservations(dataDirectory));

    public IReadOnlyList<StudentActivityObservation> LoadObservations(string dataDirectory)
        => LoadObservations(dataDirectory, RiskFeatureSchema.Withdrawal28DayV1);

    /// <summary>
    /// Loads one registered behavioral schema. The v2 cohort-relative value is calibrated only
    /// after the grouped split; this loader never uses validation/test students to derive it.
    /// </summary>
    public IReadOnlyList<StudentActivityObservation> LoadObservations(
        string dataDirectory,
        string featureSchemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"OULAD data directory was not found: {dataDirectory}");
        var schema = RiskFeatureSchemas.GetRequired(featureSchemaVersion);

        var tables = ResolveTables(dataDirectory);
        var courseLengths = LoadCourses(tables["courses.csv"]);
        var enrollments = LoadStudentInfo(tables["studentInfo.csv"], courseLengths);
        LoadRegistrations(tables["studentRegistration.csv"], enrollments);
        var missingRegistrations = enrollments.Values.Count(enrollment => !enrollment.HasRegistration);
        if (missingRegistrations > 0)
            throw new InvalidDataException($"studentRegistration.csv is missing {missingRegistrations} enrollment(s) present in studentInfo.csv.");
        var assessments = LoadAssessments(tables["assessments.csv"]);
        var siteTypes = LoadVleSites(tables["vle.csv"]);
        LoadStudentAssessments(tables["studentAssessment.csv"], assessments.ById, enrollments);
        LoadStudentVle(tables["studentVle.csv"], siteTypes, enrollments);

        var observations = BuildObservations(enrollments, assessments.ByCourse, schema);
        if (observations.Count == 0)
        {
            throw new InvalidDataException(
                "OULAD data produced no full 28-day observations. Check registration, unregistration and course-length fields.");
        }

        return observations;
    }

    private static Dictionary<string, string> ResolveTables(string directory)
    {
        var discovered = Directory.EnumerateFiles(directory, "*.csv", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var required in RequiredTables)
        {
            if (!discovered.TryGetValue(required, out var matches))
                throw new FileNotFoundException($"Required OULAD table '{required}' was not found below '{directory}'.");
            if (matches.Length != 1)
                throw new InvalidDataException($"Multiple copies of required OULAD table '{required}' were found below '{directory}'.");
            result[required] = matches[0];
        }

        return result;
    }

    private static Dictionary<CourseKey, int> LoadCourses(string path)
    {
        var courses = new Dictionary<CourseKey, int>();
        Rfc4180CsvReader.Read(path,
            ["code_module", "code_presentation", "module_presentation_length"], row =>
            {
                var key = CourseKey.From(row["code_module"], row["code_presentation"], path, row.RowNumber);
                var length = RequiredInt(row["module_presentation_length"], "module_presentation_length", path, row.RowNumber);
                if (length < ObservationWindowDays + PredictionWindowDays)
                    throw RowError(path, row.RowNumber, "module_presentation_length must be at least 56 days.");
                if (!courses.TryAdd(key, length))
                    throw RowError(path, row.RowNumber, $"duplicate course presentation '{key}'.");
            });
        return courses;
    }

    private static Dictionary<EnrollmentKey, Enrollment> LoadStudentInfo(
        string path,
        IReadOnlyDictionary<CourseKey, int> courseLengths)
    {
        var enrollments = new Dictionary<EnrollmentKey, Enrollment>();
        Rfc4180CsvReader.Read(path,
            ["code_module", "code_presentation", "id_student", "final_result"], row =>
            {
                var course = CourseKey.From(row["code_module"], row["code_presentation"], path, row.RowNumber);
                if (!courseLengths.TryGetValue(course, out var courseLength))
                    throw RowError(path, row.RowNumber, $"course presentation '{course}' is absent from courses.csv.");
                var studentId = RequiredInt(row["id_student"], "id_student", path, row.RowNumber);
                var key = new EnrollmentKey(course, studentId);
                if (!enrollments.TryAdd(key, new Enrollment(key, courseLength)))
                    throw RowError(path, row.RowNumber, $"duplicate enrollment '{key}'.");
            });
        return enrollments;
    }

    private static void LoadRegistrations(string path, IDictionary<EnrollmentKey, Enrollment> enrollments)
    {
        Rfc4180CsvReader.Read(path,
            ["code_module", "code_presentation", "id_student", "date_registration", "date_unregistration"], row =>
            {
                var key = EnrollmentKey.From(row, path);
                if (!enrollments.TryGetValue(key, out var enrollment))
                    throw RowError(path, row.RowNumber, $"registration references unknown enrollment '{key}'.");
                if (enrollment.HasRegistration)
                    throw RowError(path, row.RowNumber, $"duplicate registration for enrollment '{key}'.");
                enrollment.RegistrationDay = OptionalInt(row["date_registration"], "date_registration", path, row.RowNumber) ?? 0;
                enrollment.UnregistrationDay = OptionalInt(row["date_unregistration"], "date_unregistration", path, row.RowNumber);
                enrollment.HasRegistration = true;
            });
    }

    private static AssessmentIndex LoadAssessments(string path)
    {
        var byId = new Dictionary<int, Assessment>();
        var byCourse = new Dictionary<CourseKey, List<Assessment>>();
        Rfc4180CsvReader.Read(path,
            ["code_module", "code_presentation", "id_assessment", "date"], row =>
            {
                var course = CourseKey.From(row["code_module"], row["code_presentation"], path, row.RowNumber);
                var id = RequiredInt(row["id_assessment"], "id_assessment", path, row.RowNumber);
                var dueDay = OptionalInt(row["date"], "date", path, row.RowNumber);
                var assessment = new Assessment(id, course, dueDay);
                if (!byId.TryAdd(id, assessment))
                    throw RowError(path, row.RowNumber, $"duplicate assessment id '{id}'.");
                if (dueDay.HasValue)
                {
                    if (!byCourse.TryGetValue(course, out var list))
                        byCourse[course] = list = [];
                    list.Add(assessment);
                }
            });
        return new AssessmentIndex(byId, byCourse);
    }

    private static Dictionary<SiteKey, string> LoadVleSites(string path)
    {
        var sites = new Dictionary<SiteKey, string>();
        Rfc4180CsvReader.Read(path,
            ["id_site", "code_module", "code_presentation", "activity_type"], row =>
            {
                var course = CourseKey.From(row["code_module"], row["code_presentation"], path, row.RowNumber);
                var id = RequiredInt(row["id_site"], "id_site", path, row.RowNumber);
                var key = new SiteKey(course, id);
                if (!sites.TryAdd(key, row["activity_type"]))
                    throw RowError(path, row.RowNumber, $"duplicate VLE site '{key}'.");
            });
        return sites;
    }

    private static void LoadStudentAssessments(
        string path,
        IReadOnlyDictionary<int, Assessment> assessments,
        IDictionary<EnrollmentKey, Enrollment> enrollments)
    {
        Rfc4180CsvReader.Read(path,
            ["id_assessment", "id_student", "date_submitted", "is_banked"], row =>
            {
                var assessmentId = RequiredInt(row["id_assessment"], "id_assessment", path, row.RowNumber);
                if (!assessments.TryGetValue(assessmentId, out var assessment))
                    throw RowError(path, row.RowNumber, $"student submission references unknown assessment '{assessmentId}'.");
                var studentId = RequiredInt(row["id_student"], "id_student", path, row.RowNumber);
                var key = new EnrollmentKey(assessment.Course, studentId);
                if (!enrollments.TryGetValue(key, out var enrollment))
                    throw RowError(path, row.RowNumber, $"student submission references unknown enrollment '{key}'.");
                var submitted = RequiredInt(row["date_submitted"], "date_submitted", path, row.RowNumber);
                var isBanked = RequiredInt(row["is_banked"], "is_banked", path, row.RowNumber) != 0;
                if (!enrollment.Submissions.TryAdd(assessmentId, new Submission(submitted, isBanked)))
                {
                    throw RowError(
                        path,
                        row.RowNumber,
                        $"duplicate submission for assessment '{assessmentId}' and enrollment '{key}'.");
                }
            });
    }

    private static void LoadStudentVle(
        string path,
        IReadOnlyDictionary<SiteKey, string> sites,
        IDictionary<EnrollmentKey, Enrollment> enrollments)
    {
        Rfc4180CsvReader.Read(path,
            ["code_module", "code_presentation", "id_student", "id_site", "date", "sum_click"], row =>
            {
                var key = EnrollmentKey.From(row, path);
                if (!enrollments.TryGetValue(key, out var enrollment))
                    throw RowError(path, row.RowNumber, $"VLE activity references unknown enrollment '{key}'.");
                var siteId = RequiredInt(row["id_site"], "id_site", path, row.RowNumber);
                var siteKey = new SiteKey(key.Course, siteId);
                if (!sites.TryGetValue(siteKey, out var activityType))
                    throw RowError(path, row.RowNumber, $"VLE activity references unknown site '{siteKey}'.");
                var date = RequiredInt(row["date"], "date", path, row.RowNumber);
                var clicks = RequiredInt(row["sum_click"], "sum_click", path, row.RowNumber);
                if (clicks < 0)
                    throw RowError(path, row.RowNumber, "sum_click cannot be negative.");
                if (clicks == 0)
                    return;

                if (!enrollment.ActivityByDay.TryGetValue(date, out var activity))
                    enrollment.ActivityByDay[date] = activity = new DailyActivity();
                if (string.Equals(activityType, "forumng", StringComparison.OrdinalIgnoreCase))
                    activity.ForumClicks += clicks;
                else
                    activity.CourseClicks += clicks;
            });
    }

    private static List<StudentActivityObservation> BuildObservations(
        IReadOnlyDictionary<EnrollmentKey, Enrollment> enrollments,
        IReadOnlyDictionary<CourseKey, List<Assessment>> assessmentsByCourse,
        RiskFeatureSchemaDefinition schema)
    {
        var result = new List<StudentActivityObservation>();
        foreach (var enrollment in enrollments.Values
                     .OrderBy(value => value.Key.Course.Module, StringComparer.Ordinal)
                     .ThenBy(value => value.Key.Course.Presentation, StringComparer.Ordinal)
                     .ThenBy(value => value.Key.StudentId))
        {
            var exposureStart = Math.Max(0, enrollment.RegistrationDay);
            var firstAnchor = exposureStart + ObservationWindowDays - 1;
            var lastAnchor = enrollment.CourseLength - PredictionWindowDays;
            if (enrollment.UnregistrationDay.HasValue)
                lastAnchor = Math.Min(lastAnchor, enrollment.UnregistrationDay.Value - 1);

            assessmentsByCourse.TryGetValue(enrollment.Key.Course, out var assessments);
            assessments ??= [];

            for (var anchor = firstAnchor; anchor <= lastAnchor; anchor += ObservationStrideDays)
            {
                var windowStart = anchor - ObservationWindowDays + 1;
                var active = enrollment.ActivityByDay
                    .Where(pair => pair.Key >= windowStart && pair.Key <= anchor)
                    .OrderBy(pair => pair.Key)
                    .ToArray();

                var activeDays = active.Length;
                var firstActive = activeDays == 0 ? anchor : active[0].Key;
                var lastActive = activeDays == 0 ? windowStart - 1 : active[^1].Key;
                var forumClicks = active.Sum(pair => pair.Value.ForumClicks);
                var courseClicks = active.Sum(pair => pair.Value.CourseClicks);

                var lateOrMissing = 0;
                var assessmentsDue = 0;
                var assessmentsOnTime = 0;
                foreach (var assessment in assessments.Where(item => item.DueDay >= windowStart && item.DueDay <= anchor))
                {
                    if (enrollment.Submissions.TryGetValue(assessment.Id, out var submission) && submission.IsBanked)
                        continue;
                    assessmentsDue++;
                    if (!enrollment.Submissions.TryGetValue(assessment.Id, out submission)
                        || submission.SubmittedDay > anchor
                        || submission.SubmittedDay > assessment.DueDay)
                    {
                        lateOrMissing++;
                    }
                    else
                    {
                        assessmentsOnTime++;
                    }
                }

                var withdrawal = enrollment.UnregistrationDay.HasValue
                                 && enrollment.UnregistrationDay.Value > anchor
                                 && enrollment.UnregistrationDay.Value <= anchor + PredictionWindowDays;

                var observation = new StudentActivityObservation
                {
                    ActiveDayRate = activeDays / (float)ObservationWindowDays,
                    ActivitySpanDays = activeDays == 0 ? 0 : lastActive - firstActive + 1,
                    DaysSinceLastAccess = activeDays == 0 ? ObservationWindowDays : anchor - lastActive,
                    ForumInteractionCount = forumClicks,
                    CourseInteractionCount = courseClicks,
                    LateOrMissingAssignmentCount = lateOrMissing,
                    IsAtRisk = withdrawal,
                    StudentGroupKey = enrollment.Key.StudentId.ToString(CultureInfo.InvariantCulture),
                    EnrollmentKey = enrollment.Key.ToString(),
                    ObservationDay = anchor,
                    CoursePresentationKey = enrollment.Key.Course.ToString(),
                    WithdrawalDay = withdrawal ? enrollment.UnregistrationDay : null
                };

                if (string.Equals(schema.Version, RiskFeatureSchema.Withdrawal28DayV2, StringComparison.Ordinal))
                {
                    PopulateV2Features(
                        observation,
                        active,
                        windowStart,
                        anchor,
                        enrollment.CourseLength,
                        assessmentsDue,
                        assessmentsOnTime,
                        lateOrMissing);
                }

                result.Add(observation);
            }
        }

        return result;
    }

    private static void PopulateV2Features(
        StudentActivityObservation observation,
        IReadOnlyList<KeyValuePair<int, DailyActivity>> active,
        int windowStart,
        int anchor,
        int courseLength,
        int assessmentsDue,
        int assessmentsOnTime,
        int assessmentsLateOrMissing)
    {
        const int halfWindowDays = ObservationWindowDays / 2;
        var priorEnd = windowStart + halfWindowDays - 1;
        var recentStart = priorEnd + 1;
        var prior = active.Where(pair => pair.Key <= priorEnd).ToArray();
        var recent = active.Where(pair => pair.Key >= recentStart).ToArray();
        var priorActiveRate = prior.Length / (float)halfWindowDays;
        var recentActiveRate = recent.Length / (float)halfWindowDays;
        var priorClicks = prior.Sum(pair => pair.Value.ForumClicks + pair.Value.CourseClicks) / (float)halfWindowDays;
        var recentClicks = recent.Sum(pair => pair.Value.ForumClicks + pair.Value.CourseClicks) / (float)halfWindowDays;
        var dueRate = assessmentsDue / (float)ObservationWindowDays;

        observation.RecentActiveDayRate = recentActiveRate;
        observation.PriorActiveDayRate = priorActiveRate;
        observation.ActiveDayRateTrend = recentActiveRate - priorActiveRate;
        observation.RecentCourseClickRate = recentClicks;
        observation.PriorCourseClickRate = priorClicks;
        observation.CourseClickRateTrend = recentClicks - priorClicks;
        observation.InactivityStreakDays = LongestInactivityStreak(active, windowStart, anchor);
        observation.AssessmentDueRate = dueRate;
        observation.AssessmentOnTimeRate = assessmentsDue == 0 ? 0 : assessmentsOnTime / (float)assessmentsDue;
        observation.AssessmentLateOrMissingRate = assessmentsDue == 0 ? 0 : assessmentsLateOrMissing / (float)assessmentsDue;
        observation.CourseProgressRatio = Math.Clamp((anchor + 1) / (float)courseLength, 0, 1);
        // Fit/apply below after the grouped split. A default is not used for model training.
        observation.CohortActivityPercentile = 0;
    }

    private static int LongestInactivityStreak(
        IReadOnlyList<KeyValuePair<int, DailyActivity>> active,
        int windowStart,
        int anchor)
    {
        var activeDays = active.Select(pair => pair.Key).ToHashSet();
        var current = 0;
        var longest = 0;
        for (var day = windowStart; day <= anchor; day++)
        {
            if (activeDays.Contains(day))
            {
                current = 0;
                continue;
            }

            current++;
            if (current > longest) longest = current;
        }

        return longest;
    }

    private static int RequiredInt(string value, string column, string path, long row)
        => OptionalInt(value, column, path, row)
           ?? throw RowError(path, row, $"column '{column}' is required.");

    private static int? OptionalInt(string value, string column, string path, long row)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "?")
            return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;
        throw RowError(path, row, $"column '{column}' must be an invariant integer; received '{value}'.");
    }

    private static InvalidDataException RowError(string path, long row, string detail)
        => new($"OULAD table '{Path.GetFileName(path)}' row {row}: {detail}");

    private readonly record struct CourseKey(string Module, string Presentation)
    {
        public static CourseKey From(string module, string presentation, string path, long row)
        {
            if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(presentation))
                throw RowError(path, row, "code_module and code_presentation are required.");
            return new CourseKey(module.Trim(), presentation.Trim());
        }

        public override string ToString() => $"{Module}/{Presentation}";
    }

    private readonly record struct EnrollmentKey(CourseKey Course, int StudentId)
    {
        public static EnrollmentKey From(CsvRecord row, string path)
            => new(
                CourseKey.From(row["code_module"], row["code_presentation"], path, row.RowNumber),
                RequiredInt(row["id_student"], "id_student", path, row.RowNumber));

        public override string ToString() => $"{Course}/{StudentId.ToString(CultureInfo.InvariantCulture)}";
    }

    private readonly record struct SiteKey(CourseKey Course, int SiteId);
    private sealed record Assessment(int Id, CourseKey Course, int? DueDay);
    private sealed record AssessmentIndex(
        Dictionary<int, Assessment> ById,
        Dictionary<CourseKey, List<Assessment>> ByCourse);
    private sealed record Submission(int SubmittedDay, bool IsBanked);

    private sealed class DailyActivity
    {
        public long ForumClicks { get; set; }
        public long CourseClicks { get; set; }
    }

    private sealed class Enrollment
    {
        public Enrollment(EnrollmentKey key, int courseLength)
        {
            Key = key;
            CourseLength = courseLength;
        }

        public EnrollmentKey Key { get; }
        public int CourseLength { get; }
        public int RegistrationDay { get; set; }
        public int? UnregistrationDay { get; set; }
        public bool HasRegistration { get; set; }
        public Dictionary<int, DailyActivity> ActivityByDay { get; } = [];
        public Dictionary<int, Submission> Submissions { get; } = [];
    }
}
