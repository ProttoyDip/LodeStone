using System.Globalization;
using System.Text;
using Lodestone.Domain.Enums;

namespace Lodestone.Application.Services;

/// <summary>
/// Assembles the opening of a counselor's session note from the appointment record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Template, not language model.</b> Every line is either a fact the booking system already
/// holds -- when the session was, whether it happened, how many times this counselor has seen this
/// student -- or a labelled blank for the counselor to fill in. Nothing here is inferred, and no
/// text is generated from anything the student wrote.
/// </para>
/// <para>
/// <b>The student's words are not copied in.</b> The booking note the student left is visible on
/// the same screen; the draft points at it and leaves the counselor to paraphrase. A clinical
/// record should be the counselor's account, and a pre-filled quotation is easy to leave in by
/// accident.
/// </para>
/// <para>
/// <b>It is a starting point the counselor overwrites.</b> The draft is offered only into an empty
/// notes box and is never saved on its own: what reaches the record is whatever the counselor
/// submits.
/// </para>
/// </remarks>
public static class SessionReportDrafter
{
    /// <summary>What the drafter knows about a session. All of it is structural.</summary>
    /// <param name="PriorCompletedSessions">Completed sessions between this counselor and student before this one.</param>
    /// <param name="LastCompletedSessionUtc">When the most recent of those took place, if any.</param>
    public sealed record SessionFacts(
        DateTime StartUtc,
        DateTime EndUtc,
        DateTime BookedAtUtc,
        bool StudentLeftBookingNote,
        int PriorCompletedSessions,
        DateTime? LastCompletedSessionUtc);

    public const string Placeholder = "[ ]";

    public static string Draft(SessionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var text = new StringBuilder();
        text.Append("Session ")
            .Append(facts.StartUtc.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture))
            .Append('\u2013')
            .Append(facts.EndUtc.ToString("HH:mm", CultureInfo.InvariantCulture))
            .Append(" UTC (")
            .Append(FormatDuration(facts.EndUtc - facts.StartUtc))
            .AppendLine(").");

        text.Append("Booked ")
            .Append(facts.BookedAtUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture))
            .Append(". ")
            .AppendLine(DescribeHistory(facts));

        text.AppendLine();
        text.Append("Reason for booking (in your words")
            .Append(facts.StudentLeftBookingNote ? "; the student's note is shown above" : string.Empty)
            .Append("): ").AppendLine(Placeholder);
        text.Append("What we discussed: ").AppendLine(Placeholder);
        text.Append("Agreed next steps: ").AppendLine(Placeholder);
        text.Append("Follow-up: ").Append(Placeholder);

        return text.ToString();
    }

    private static string DescribeHistory(SessionFacts facts)
    {
        if (facts.PriorCompletedSessions <= 0)
            return "First completed session with this counselor.";

        var ordinal = Ordinal(facts.PriorCompletedSessions + 1);
        var last = facts.LastCompletedSessionUtc is { } lastUtc
            ? $" Previous session {lastUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
            : string.Empty;
        return $"{ordinal} session with this counselor.{last}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes);
        return minutes <= 0 ? "duration not recorded" : $"{minutes.ToString(CultureInfo.InvariantCulture)} min";
    }

    private static string Ordinal(int number)
    {
        var suffix = (number % 100) is 11 or 12 or 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
