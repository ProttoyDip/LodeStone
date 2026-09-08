using System.Text.RegularExpressions;
using Lodestone.Application.DTOs.Crisis;
using Lodestone.Domain.Entities;

namespace Lodestone.Application.Services;

/// <summary>
/// Finds the crisis resources whose own text best matches what a person wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retrieval only.</b> This is the retrieval half of a retrieval-augmented design with the
/// generation half deliberately left out. Every result is an existing resource shown verbatim. No
/// sentence is composed, no advice is produced, and the words a person typed are used to rank and
/// then discarded -- they are never stored, logged or sent anywhere.
/// </para>
/// <para>
/// <b>Not a classifier.</b> The query-expansion lexicon maps everyday words to the vocabulary the
/// resources use ("drinking" to "substance", "therapist" to "counselor"). It does not assign the
/// person a category, and a poor match means only that the search vocabulary fell short. The page
/// that calls this always shows the emergency resources regardless of the ranking, so a missed
/// match can never hide the way to urgent help.
/// </para>
/// <para>
/// <b>Local and deterministic.</b> BM25 over title and description, computed in-process. The same
/// words always produce the same order, and an administrator can read exactly why.
/// </para>
/// </remarks>
public static class CrisisResourceRetriever
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    /// <summary>Title words count this many times: the title is what a resource is for.</summary>
    private const int TitleRepeat = 2;

    private static readonly Regex Tokenizer = new(@"[a-z0-9]+", RegexOptions.Compiled);

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "the", "for", "with", "have", "has", "had", "are", "was", "were", "can",
        "could", "would", "should", "will", "any", "all", "some", "this", "that", "these", "those",
        "from", "about", "into", "than", "then", "them", "they", "you", "your", "our", "his", "her",
        "its", "who", "what", "when", "where", "how", "why", "not", "but", "get", "got", "like",
        "want", "also", "very", "much", "more", "most", "just", "been", "being", "over", "i", "me",
        "my", "am", "im", "ive", "is", "it", "to", "of", "in", "on", "at", "be", "do", "so", "if",
        "or", "as", "up", "out", "no", "yes", "feel", "feeling", "feels", "felt", "need", "needs",
        "someone", "something", "anyone", "anything", "really", "keep", "cant", "dont", "know"
    };

    /// <summary>
    /// Everyday words mapped to the words the resources use. Values are resource vocabulary, not
    /// descriptions of the person; the lexicon exists so "drinking" can find a substance-use line.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Expansions = BuildExpansions(
        (["text", "texting", "sms", "message", "messaging", "type", "typing", "chat", "chatting"],
            ["text", "crisis", "counselor"]),
        (["call", "calling", "phone", "hotline", "helpline", "lifeline", "line", "talk", "talking", "speak", "voice"],
            ["call", "helpline", "lifeline", "phone"]),
        (["lgbtq", "lgbt", "gay", "lesbian", "trans", "transgender", "queer", "bisexual", "nonbinary", "pride", "identity"],
            ["lgbtq", "youth", "trevor"]),
        (["drink", "drinking", "drunk", "alcohol", "drug", "drugs", "weed", "pills", "high", "addiction", "addicted", "substance", "using", "relapse"],
            ["substance", "use", "treatment", "referral"]),
        (["therapy", "therapist", "counsellor", "counselor", "counselling", "counseling", "appointment", "session", "book", "booking", "psychologist"],
            ["counselor", "counseling", "session", "book"]),
        (["campus", "university", "uni", "college", "school", "lecturer", "tutor", "course", "exam", "exams", "deadline", "deadlines"],
            ["campus", "counseling", "lodestone"]),
        (["anxious", "anxiety", "panic", "panicking", "depressed", "depression", "lonely", "alone", "hopeless", "worthless", "overwhelmed", "overwhelming", "stress", "stressed", "sad", "cry", "crying", "sleep", "sleeping", "insomnia", "numb", "scared", "afraid", "worried", "worry", "burnout", "exhausted"],
            ["mental", "health", "distress", "support", "information"]),
        (["suicide", "suicidal", "die", "dying", "kill", "death", "dead", "end", "hurt", "hurting", "harm", "harming", "selfharm", "cut", "cutting", "overdose", "unsafe", "danger", "dangerous", "emergency", "urgent", "immediate", "immediately"],
            ["crisis", "suicide", "emergency", "immediate", "danger", "distress"]),
        (["info", "information", "learn", "understand", "resources", "resource", "local", "family", "parent", "parents", "friend", "friends", "partner", "roommate"],
            ["information", "resources", "local", "support"]),
        (["confidential", "private", "privately", "anonymous", "anonymously", "secret", "free", "cost", "money", "afford"],
            ["confidential", "free"]),
        (["night", "late", "now", "tonight", "weekend", "always", "hours", "midnight", "morning", "today"],
            ["24", "7", "available"]));

    /// <summary>
    /// Resources ranked by relevance to <paramref name="query"/>, best first. Resources with no
    /// overlap are omitted; an empty result means the vocabulary did not match, nothing more.
    /// </summary>
    public static IReadOnlyList<CrisisResourceMatch> Rank(string? query, IReadOnlyList<CrisisResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var queryTerms = ExpandQuery(query);
        if (queryTerms.Count == 0 || resources.Count == 0)
            return Array.Empty<CrisisResourceMatch>();

        var documents = resources.Select(Document.From).ToArray();
        var averageLength = documents.Average(document => document.Length);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
            foreach (var term in document.TermFrequency.Keys)
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;

        var results = new List<CrisisResourceMatch>();
        foreach (var document in documents)
        {
            var score = 0d;
            var matched = new List<string>();
            foreach (var term in queryTerms)
            {
                if (!document.TermFrequency.TryGetValue(term, out var frequency)) continue;

                var idf = Math.Log(1 + (documents.Length - documentFrequency[term] + 0.5) / (documentFrequency[term] + 0.5));
                var normalized = frequency * (K1 + 1) / (frequency + K1 * (1 - B + B * document.Length / averageLength));
                score += idf * normalized;
                matched.Add(term);
            }

            if (score <= 0) continue;
            results.Add(new CrisisResourceMatch(
                document.Resource,
                Math.Round(score, 4),
                matched.OrderBy(term => term, StringComparer.Ordinal).ToArray()));
        }

        return results
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Resource.IsEmergency)
            .ThenBy(match => match.Resource.DisplayOrder)
            .ToArray();
    }

    private static HashSet<string> ExpandQuery(string? query)
    {
        var terms = Tokenize(query);
        foreach (var term in terms.ToArray())
            if (Expansions.TryGetValue(term, out var related))
                terms.UnionWith(related);
        return terms;
    }

    private static HashSet<string> Tokenize(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        foreach (Match match in Tokenizer.Matches(text.ToLowerInvariant().Replace("'", string.Empty)))
        {
            var token = match.Value;
            if (Stopwords.Contains(token)) continue;
            // "988", "911", "24" and "7" are meaningful here even though they are short.
            if (token.Length < 2 && !char.IsDigit(token[0])) continue;
            tokens.Add(token);
        }

        return tokens;
    }

    private static IReadOnlyDictionary<string, string[]> BuildExpansions(params (string[] Words, string[] Vocabulary)[] groups)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (words, vocabulary) in groups)
            foreach (var word in words)
                map[word] = map.TryGetValue(word, out var existing)
                    ? existing.Concat(vocabulary).Distinct(StringComparer.Ordinal).ToArray()
                    : vocabulary;
        return map;
    }

    private sealed record Document(CrisisResource Resource, IReadOnlyDictionary<string, int> TermFrequency, int Length)
    {
        public static Document From(CrisisResource resource)
        {
            var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
            var length = 0;

            void Add(string? text, int repeat)
            {
                foreach (Match match in Tokenizer.Matches((text ?? string.Empty).ToLowerInvariant()))
                {
                    var token = match.Value;
                    if (Stopwords.Contains(token)) continue;
                    frequency[token] = frequency.GetValueOrDefault(token) + repeat;
                    length += repeat;
                }
            }

            Add(resource.Title, TitleRepeat);
            Add(resource.Description, 1);
            Add(resource.PhoneNumber, 1);
            return new Document(resource, frequency, Math.Max(1, length));
        }
    }
}
