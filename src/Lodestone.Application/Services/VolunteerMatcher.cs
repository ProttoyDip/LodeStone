using System.Globalization;
using System.Text.RegularExpressions;
using Lodestone.Application.DTOs.Volunteer;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>
/// Ranks volunteers by how well they fit a support request.
/// </summary>
/// <remarks>
/// <para>
/// This is a recommendation to an administrator and never an assignment. Matching a distressed
/// student to a stranger is a judgement about two people, and the system does not have the context
/// to make it -- it only has the context to put the plausible options at the top of the list.
/// </para>
/// <para>
/// Everything here is local, deterministic and inspectable: overlapping words, declared
/// availability, and current workload. No model, no embedding service, nothing leaves the process.
/// A ranking an administrator cannot audit would be worse than the alphabetical list it replaces,
/// because it would look authoritative.
/// </para>
/// </remarks>
public static class VolunteerMatcher
{
    /// <summary>Weight of skill and interest overlap. The dominant signal: it is what was asked for.</summary>
    private const double SkillWeight = 0.55;

    /// <summary>Weight of overlapping stated availability.</summary>
    private const double AvailabilityWeight = 0.25;

    /// <summary>
    /// Weight of having capacity. Deliberately present and deliberately small: spreading work stops
    /// one willing volunteer absorbing every request until they burn out, but it must never
    /// outrank actually being able to help.
    /// </summary>
    private const double CapacityWeight = 0.20;

    /// <summary>Assignments at which a volunteer is treated as fully loaded.</summary>
    private const int FullWorkload = 5;

    private static readonly Regex Tokenizer = new(@"[a-z0-9#+.]+", RegexOptions.Compiled);

    /// <summary>
    /// Words too common to indicate a match. Without this, "support" in a volunteer's bio would
    /// score against every request, and the ranking would flatten into noise.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "and", "the", "for", "with", "have", "has", "had", "are", "was", "were", "can", "could",
        "would", "should", "will", "any", "all", "some", "this", "that", "these", "those", "from",
        "about", "into", "than", "then", "them", "they", "you", "your", "our", "his", "her", "its",
        "who", "what", "when", "where", "how", "why", "not", "but", "get", "got", "help", "helping",
        "support", "supporting", "student", "students", "university", "please", "need", "needs",
        "like", "want", "also", "very", "much", "more", "most", "just", "been", "being", "over"
    };

    /// <summary>
    /// Terms that describe each request category, so a category alone still routes sensibly when a
    /// student writes only a sentence. These describe the kind of help asked for -- never the
    /// student.
    /// </summary>
    private static readonly IReadOnlyDictionary<SupportRequestCategory, string[]> CategoryTerms =
        new Dictionary<SupportRequestCategory, string[]>
        {
            [SupportRequestCategory.AcademicGuidance] =
                ["academic", "study", "studying", "course", "module", "coursework", "assignment",
                 "revision", "exam", "tutoring", "elective", "timetable"],
            [SupportRequestCategory.CampusAdjustment] =
                ["campus", "settling", "orientation", "accommodation", "homesick", "international",
                 "society", "societies", "belonging", "transition"],
            [SupportRequestCategory.PeerDiscussion] =
                ["peer", "listening", "mentoring", "conversation", "wellbeing", "buddy"],
            [SupportRequestCategory.TechnicalHelp] =
                ["technical", "software", "hardware", "laptop", "login", "portal", "programming",
                 "code", "coding", "python", "java", "c#", "javascript", "database", "network"],
            [SupportRequestCategory.GeneralSupport] = []
        };

    /// <summary>
    /// Ranks candidates best-first. Volunteers with nothing in common with the request still
    /// appear, scored on capacity alone, so an administrator is never left with an empty list.
    /// </summary>
    public static IReadOnlyList<VolunteerMatch> Rank(
        VolunteerMatchRequest request,
        IReadOnlyList<VolunteerMatchCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);

        var wanted = BuildRequestTerms(request);
        var wantedAvailability = Tokenize(request.Availability);

        return candidates
            .Select(candidate => Score(candidate, wanted, wantedAvailability))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static VolunteerMatch Score(
        VolunteerMatchCandidate candidate,
        IReadOnlySet<string> wanted,
        IReadOnlySet<string> wantedAvailability)
    {
        var reasons = new List<string>();

        // Skills carry more meaning than a bio, but a volunteer who described their experience in
        // prose rather than filling in the skills box should not be invisible.
        var offered = Tokenize(candidate.Skills);
        offered.UnionWith(Tokenize(candidate.Department));
        offered.UnionWith(Tokenize(candidate.Bio));

        var matchedSkills = wanted.Intersect(offered, StringComparer.Ordinal)
            .OrderBy(term => term, StringComparer.Ordinal)
            .ToArray();
        var skillScore = wanted.Count == 0
            ? 0d
            : Math.Min(1d, matchedSkills.Length / (double)Math.Min(wanted.Count, 6));
        if (matchedSkills.Length > 0)
        {
            reasons.Add($"Matches on {string.Join(", ", matchedSkills.Take(4))}");
        }

        var matchedAvailability = wantedAvailability
            .Intersect(Tokenize(candidate.Availability), StringComparer.Ordinal)
            .ToArray();
        var availabilityScore = wantedAvailability.Count == 0
            ? 0d
            : Math.Min(1d, matchedAvailability.Length / (double)Math.Min(wantedAvailability.Count, 3));
        if (matchedAvailability.Length > 0)
        {
            reasons.Add($"Available {string.Join(", ", matchedAvailability.Take(3))}");
        }

        var load = Math.Max(0, candidate.OpenAssignmentCount);
        var capacityScore = load >= FullWorkload ? 0d : (FullWorkload - load) / (double)FullWorkload;
        reasons.Add(load switch
        {
            0 => "No students currently assigned",
            1 => "1 student currently assigned",
            _ => $"{load.ToString(CultureInfo.InvariantCulture)} students currently assigned"
        });

        var score = (skillScore * SkillWeight)
                    + (availabilityScore * AvailabilityWeight)
                    + (capacityScore * CapacityWeight);

        return new VolunteerMatch(
            candidate.VolunteerProfileId,
            candidate.FullName,
            Math.Round(score, 4),
            reasons);
    }

    private static HashSet<string> BuildRequestTerms(VolunteerMatchRequest request)
    {
        var terms = Tokenize(request.Message);
        if (CategoryTerms.TryGetValue(request.Category, out var categoryTerms))
            terms.UnionWith(categoryTerms);
        return terms;
    }

    private static HashSet<string> Tokenize(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return tokens;

        foreach (Match match in Tokenizer.Matches(value.ToLowerInvariant()))
        {
            var token = match.Value.Trim('.');
            // Two-character tokens are kept only when they carry meaning, such as "c#".
            if (token.Length < 3 && !token.Contains('#') && !token.Contains('+')) continue;
            if (Stopwords.Contains(token)) continue;
            tokens.Add(token);
        }

        return tokens;
    }
}
