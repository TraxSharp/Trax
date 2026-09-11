namespace Trax.Adr.Guard;

/// <summary>
/// Judging whether an opt-out gives a reason or a deferral. Shared by the two places that
/// accept one, so they cannot drift into disagreeing about what counts.
/// </summary>
/// <remarks>
/// Opting out is a normal and useful answer: a framework choice is not a rule code can
/// break, and a rule about how rules are written cannot check itself. What is rejected is
/// the answer that is really "we have not got to it", because a rule that should be
/// recorded and is not yet should be recorded, not exempted.
/// </remarks>
public static class Reasons
{
    public const int MinimumLength = 40;

    /// <summary>
    /// Phrasings that defer rather than explain. <c>yet</c> stands alone deliberately:
    /// "we have not written the guard yet" is the same deferral as "not yet", and the
    /// words in between meant a phrase-only pattern missed it.
    /// </summary>
    private static readonly Regex Hollow = new(
        @"\b(yet|todo|tbd|later|for now|coming soon|eventually|someday|will be (written|added|done)|to be (written|added|done)|follow[- ]up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>Null when the reason is acceptable, otherwise why it is not.</summary>
    public static string? Problem(string reason)
    {
        if (reason.Length < MinimumLength)
            return $"needs a real reason, not '{reason}'";

        return Hollow.IsMatch(reason)
            ? $"'{Truncate(reason)}' reads as a deferral. A rule that should be recorded and is "
                + "not yet should be recorded, not exempted."
            : null;
    }

    public static string Truncate(string text) => text.Length <= 60 ? text : text[..60] + "...";
}
