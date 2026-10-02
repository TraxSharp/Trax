using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// The tracks a routing step declares, shared by <see cref="Tracks{TInput, TReturn, TTrack}"/>,
/// <see cref="GateTracks{TInput, TReturn}"/> and <see cref="ScaleTracks{TInput, TReturn, TLevel}"/>.
/// </summary>
internal sealed class TrackSet<TInput, TReturn>
{
    public List<DeclaredTrack<TInput, TReturn>> Tracks { get; } = [];

    public DeclaredTrack<TInput, TReturn>? Fallback { get; private set; }

    public List<string> Problems { get; } = [];

    /// <summary>
    /// The shadow deciders the step's own question is also put to. Checked where the question is
    /// built, by <see cref="Questions{TState}"/>, so a mistake is reported once.
    /// </summary>
    public List<Type> Shadows { get; } = [];

    /// <summary>How long to wait for the shadows, when the declaration says.</summary>
    public TimeSpan? ShadowWait { get; set; }

    /// <summary>
    /// The refusal for shadows declared on a routing step that asks nothing, because it routes on
    /// a decision made earlier.
    /// </summary>
    public string? ShadowsWithoutAQuestion =>
        Shadows.Count > 0 || ShadowWait is not null
            ? "declares shadows, but routes on a decision made earlier and asks nothing to "
                + "compare. Shadow the Decide that asks it."
            : null;

    public void Add(DeclaredTrack<TInput, TReturn> track)
    {
        if (Tracks.Any(t => t.Name == track.Name))
            Problems.Add($"declares the track '{track.Name}' twice. Declare each track once.");

        Tracks.Add(track);
    }

    public void SetFallback(
        string name,
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> body
    )
    {
        if (Fallback is not null)
            Problems.Add($"declares {name} twice. Declare one.");

        Fallback = new DeclaredTrack<TInput, TReturn>(name, null, body, null);
    }

    public static string? ConfidenceProblem(double? value) =>
        value is { } v && (double.IsNaN(v) || v < 0 || v > 1)
            ? $"requires a confidence of {QuestionSpec.Format(v)}, which is not between 0 and 1. "
                + "Confidence is a probability."
            : null;

    /// <summary>
    /// The refusal for a track declared on a value <typeparamref name="TEnum"/> does not define,
    /// such as <c>(Lane)7</c>. No answer can name it, and offering it would offer something else.
    /// </summary>
    public static string? NotAMember<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? null
            : $"declares a track for '{value}', which is not a member of "
                + $"'{typeof(TEnum).ReadableName()}'. Declare tracks on its members.";

    /// <summary>Every track, with the fallback last.</summary>
    public IEnumerable<DeclaredTrack<TInput, TReturn>> All =>
        Fallback is null ? Tracks : Tracks.Append(Fallback);
}

/// <summary>One declared track: its name, what it is for, its body, and its own confidence bar.</summary>
internal sealed record DeclaredTrack<TInput, TReturn>(
    string Name,
    string? Description,
    Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> Body,
    double? RequireConfidence
);

/// <summary>
/// Declares the tracks of a <c>Switch</c>: one per <typeparamref name="TTrack"/> member the
/// decision may choose, and optionally an <see cref="Otherwise"/> track for when the decision is
/// not followed.
/// </summary>
/// <remarks>
/// Each track is written the way the rest of the chain is, on the parameter it is handed
/// (<c>t =&gt; t.Chain&lt;IssueRefund&gt;()</c>). Every track is read when the chain is declared and
/// verified at startup; only the chosen one runs. A track may be empty (<c>t =&gt; t</c>).
/// </remarks>
public sealed class Tracks<TInput, TReturn, TTrack>
    where TTrack : struct, Enum
{
    internal Tracks() { }

    internal TrackSet<TInput, TReturn> Set { get; } = new();

    internal double MinimumConfidence { get; private set; }

    /// <summary>
    /// Declares the track the train takes when the decision is <paramref name="track"/>.
    /// </summary>
    /// <param name="track">The member that selects this track.</param>
    /// <param name="then">The junctions this track runs.</param>
    /// <param name="description">
    /// What the track is for, offered to the decider when the switch asks its own question.
    /// Defaults to the member's <see cref="System.ComponentModel.DescriptionAttribute"/>.
    /// </param>
    /// <param name="requireConfidence">
    /// The confidence this track needs, overriding <see cref="RequireConfidence"/>. Give the track
    /// with the gravest consequence the highest bar, and the safest the lowest.
    /// </param>
    public Tracks<TInput, TReturn, TTrack> When(
        TTrack track,
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then,
        string? description = null,
        double? requireConfidence = null
    )
    {
        if (TrackSet<TInput, TReturn>.ConfidenceProblem(requireConfidence) is { } problem)
            Set.Problems.Add($"track '{track}' {problem}");

        if (TrackSet<TInput, TReturn>.NotAMember(track) is { } undefined)
            Set.Problems.Add(undefined);

        Set.Add(
            new DeclaredTrack<TInput, TReturn>(
                track.ToString(),
                description,
                then,
                requireConfidence
            )
        );
        return this;
    }

    /// <summary>
    /// Declares where the train goes when the decision chooses a member this switch has no track
    /// for, or chooses one with less confidence than that track requires. Without it, either
    /// case fails the run.
    /// </summary>
    public Tracks<TInput, TReturn, TTrack> Otherwise(
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then
    )
    {
        Set.SetFallback("Otherwise", then);
        return this;
    }

    /// <summary>
    /// The confidence, from 0 to 1, below which a choice is not followed, for every track that
    /// does not set its own. Defaults to 0.
    /// </summary>
    public Tracks<TInput, TReturn, TTrack> RequireConfidence(double minimum)
    {
        if (TrackSet<TInput, TReturn>.ConfidenceProblem(minimum) is { } problem)
            Set.Problems.Add(problem);
        else
            MinimumConfidence = minimum;

        return this;
    }

    /// <summary>
    /// Also puts this step's question to <typeparamref name="TDecider"/>, and records whether it
    /// would have sent the train down the same track, without acting on its answer. Only for the
    /// form of this step that asks its own question.
    /// </summary>
    /// <remarks>
    /// Agreement is judged by this step's own tracks, bars and bands. A shadow that fails, is slow
    /// or disagrees never changes the run; see <see cref="Questions{TState}.Shadow{TDecider}"/>.
    /// </remarks>
    public Tracks<TInput, TReturn, TTrack> Shadow<TDecider>()
        where TDecider : class, IDecider
    {
        Set.Shadows.Add(typeof(TDecider));
        return this;
    }

    /// <summary>
    /// How long to wait for the shadows once the live answer is in; see
    /// <see cref="Questions{TState}.WaitForShadows"/>.
    /// </summary>
    public Tracks<TInput, TReturn, TTrack> WaitForShadows(TimeSpan wait)
    {
        Set.ShadowWait = wait;
        return this;
    }

    /// <summary>
    /// The track a choice takes, and why the choice was not followed when the fallback is taken.
    /// Null when there is no track to take. Running a switch and comparing a shadow's answer both
    /// route through here, so they cannot disagree about where a choice goes.
    /// </summary>
    internal (DeclaredTrack<TInput, TReturn>? Taken, string? FallbackReason) Route(
        ChoiceDecision<TTrack> decision
    )
    {
        var chosen = Set.Tracks.Find(t => t.Name == decision.Choice.ToString());
        var required = chosen?.RequireConfidence ?? MinimumConfidence;

        var reason =
            chosen is null ? $"the decision was '{decision.Choice}', which has no track here"
            : decision.Confidence < required
                ? $"the decision was '{decision.Choice}' with a confidence of "
                    + $"{QuestionSpec.Format(decision.Confidence)}, below the "
                    + $"{QuestionSpec.Format(required)} its track requires"
            : null;

        return (reason is null ? chosen : Set.Fallback, reason);
    }

    internal IReadOnlyList<(TTrack, string?)> Offered =>
        Set
            .Tracks.Select(t =>
                (EnumMembers<TTrack>.TryParse(t.Name, out var v) ? v : default, t.Description)
            )
            .ToList();
}

/// <summary>
/// Declares the tracks of a <c>Gate</c>, which routes on the probability that the answer to a
/// yes/no question is yes.
/// </summary>
/// <remarks>
/// A probability at or above the <see cref="Yes"/> bar takes the yes track; one below the
/// <see cref="No"/> bar takes the no track; anything between takes <see cref="Unsure"/>, or fails
/// the run when no unsure track is declared. Both bars default to 0.5, which leaves no band
/// between them.
/// </remarks>
public sealed class GateTracks<TInput, TReturn>
{
    internal GateTracks() { }

    internal TrackSet<TInput, TReturn> Set { get; } = new();

    internal double YesAtLeast { get; private set; } = 0.5;

    internal double NoBelow { get; private set; } = 0.5;

    internal DeclaredTrack<TInput, TReturn>? YesTrack => Set.Tracks.Find(t => t.Name == "Yes");

    internal DeclaredTrack<TInput, TReturn>? NoTrack => Set.Tracks.Find(t => t.Name == "No");

    internal IEnumerable<string> Problems
    {
        get
        {
            foreach (var problem in Set.Problems)
                yield return problem;

            if (YesTrack is null)
                yield return "declares no Yes track. Declare one, even if empty.";

            if (NoTrack is null)
                yield return "declares no No track. Declare one, even if empty.";

            if (NoBelow > YesAtLeast)
                yield return $"takes No below {QuestionSpec.Format(NoBelow)}, above where it takes "
                    + $"Yes ({QuestionSpec.Format(YesAtLeast)}), so some probabilities would take "
                    + "both.";
        }
    }

    /// <summary>
    /// Also puts this step's question to <typeparamref name="TDecider"/>, and records whether it
    /// would have sent the train down the same track, without acting on its answer. Only for the
    /// form of this step that asks its own question.
    /// </summary>
    /// <remarks>
    /// Agreement is judged by this step's own tracks, bars and bands. A shadow that fails, is slow
    /// or disagrees never changes the run; see <see cref="Questions{TState}.Shadow{TDecider}"/>.
    /// </remarks>
    public GateTracks<TInput, TReturn> Shadow<TDecider>()
        where TDecider : class, IDecider
    {
        Set.Shadows.Add(typeof(TDecider));
        return this;
    }

    /// <summary>
    /// How long to wait for the shadows once the live answer is in; see
    /// <see cref="Questions{TState}.WaitForShadows"/>.
    /// </summary>
    public GateTracks<TInput, TReturn> WaitForShadows(TimeSpan wait)
    {
        Set.ShadowWait = wait;
        return this;
    }

    /// <summary>
    /// The track a probability of yes takes, or null when it falls between the bars and there is
    /// no Unsure track. Unsure is a declared outcome, not a decision overruled, so it carries no
    /// fallback reason.
    /// </summary>
    internal DeclaredTrack<TInput, TReturn>? Route(double probability) =>
        probability >= YesAtLeast ? YesTrack
        : probability < NoBelow ? NoTrack
        : Set.Fallback;

    /// <summary>The track taken when the probability of yes is at least <paramref name="atLeast"/>.</summary>
    public GateTracks<TInput, TReturn> Yes(
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then,
        double atLeast = 0.5
    )
    {
        if (double.IsNaN(atLeast) || atLeast <= 0 || atLeast > 1)
            Set.Problems.Add(
                $"takes Yes at {QuestionSpec.Format(atLeast)}, which is not above 0 and at most 1."
            );
        else
            YesAtLeast = atLeast;

        Set.Add(new DeclaredTrack<TInput, TReturn>("Yes", null, then, null));
        return this;
    }

    /// <summary>The track taken when the probability of yes is below <paramref name="below"/>.</summary>
    public GateTracks<TInput, TReturn> No(
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then,
        double below = 0.5
    )
    {
        if (double.IsNaN(below) || below < 0 || below >= 1)
            Set.Problems.Add(
                $"takes No below {QuestionSpec.Format(below)}, which is not at least 0 and "
                    + "below 1."
            );
        else
            NoBelow = below;

        Set.Add(new DeclaredTrack<TInput, TReturn>("No", null, then, null));
        return this;
    }

    /// <summary>The track taken when the probability falls between the No and Yes bars.</summary>
    public GateTracks<TInput, TReturn> Unsure(
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then
    )
    {
        Set.SetFallback("Unsure", then);
        return this;
    }
}

/// <summary>
/// Declares the tracks of a <c>Scale</c>, which routes on where the state falls on the ordered
/// levels of <typeparamref name="TLevel"/>.
/// </summary>
/// <remarks>
/// The score rounds to its nearest level, and the train takes the track declared for the highest
/// level at or below it. The lowest level must have a track, so every score has one.
/// </remarks>
public sealed class ScaleTracks<TInput, TReturn, TLevel>
    where TLevel : struct, Enum
{
    internal ScaleTracks() { }

    internal TrackSet<TInput, TReturn> Set { get; } = new();

    internal double MinimumConfidence { get; private set; }

    internal IEnumerable<string> Problems
    {
        get
        {
            foreach (var problem in Set.Problems)
                yield return problem;

            // An enum with no members has no lowest level; TooFewLevels reports it.
            if (EnumMembers<TLevel>.Ordered.Count == 0)
                yield break;

            var lowest = EnumMembers<TLevel>.Ordered[0];

            if (!Set.Tracks.Any(t => t.Name == lowest.ToString()))
                yield return $"has no track from its lowest level, '{lowest}', so a low score has "
                    + "nowhere to go. Declare AtLeast for it.";
        }
    }

    /// <summary>
    /// The refusal for a scale on fewer than two levels, which no score can place anything on. The
    /// form that asks its own question reports it through the question instead, so it is said
    /// once.
    /// </summary>
    internal static string? TooFewLevels =>
        EnumMembers<TLevel>.Ordered.Count < 2
            ? $"routes on '{typeof(TLevel).ReadableName()}', which has fewer than two levels, so "
                + "no score can be placed on it."
            : null;

    /// <summary>
    /// Also puts this step's question to <typeparamref name="TDecider"/>, and records whether it
    /// would have sent the train down the same track, without acting on its answer. Only for the
    /// form of this step that asks its own question.
    /// </summary>
    /// <remarks>
    /// Agreement is judged by this step's own tracks, bars and bands. A shadow that fails, is slow
    /// or disagrees never changes the run; see <see cref="Questions{TState}.Shadow{TDecider}"/>.
    /// </remarks>
    public ScaleTracks<TInput, TReturn, TLevel> Shadow<TDecider>()
        where TDecider : class, IDecider
    {
        Set.Shadows.Add(typeof(TDecider));
        return this;
    }

    /// <summary>
    /// How long to wait for the shadows once the live answer is in; see
    /// <see cref="Questions{TState}.WaitForShadows"/>.
    /// </summary>
    public ScaleTracks<TInput, TReturn, TLevel> WaitForShadows(TimeSpan wait)
    {
        Set.ShadowWait = wait;
        return this;
    }

    /// <summary>
    /// The track a score takes: the one for the highest level at or below the level it rounds to,
    /// or the fallback, with why, when its confidence is below the bar.
    /// </summary>
    internal (DeclaredTrack<TInput, TReturn>? Taken, string? FallbackReason) Route(
        ScoreDecision<TLevel> decision
    )
    {
        var reached = EnumMembers<TLevel>.IndexOf(decision.Nearest);

        var band = Set
            .Tracks.Select(t =>
                (
                    Track: t,
                    Index: EnumMembers<TLevel>.TryParse(t.Name, out var level)
                        ? EnumMembers<TLevel>.IndexOf(level)
                        : int.MaxValue
                )
            )
            .Where(b => b.Index <= reached)
            .MaxBy(b => b.Index)
            .Track;

        var reason =
            decision.Confidence < MinimumConfidence
                ? $"the score {QuestionSpec.Format(decision.Score)} came with a confidence of "
                    + $"{QuestionSpec.Format(decision.Confidence)}, below the "
                    + $"{QuestionSpec.Format(MinimumConfidence)} this scale requires"
                : null;

        return (reason is null ? band : Set.Fallback, reason);
    }

    /// <summary>
    /// Declares the track for a score that rounds to <paramref name="level"/> or above, up to the
    /// next level with a track of its own.
    /// </summary>
    public ScaleTracks<TInput, TReturn, TLevel> AtLeast(
        TLevel level,
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then
    )
    {
        if (TrackSet<TInput, TReturn>.NotAMember(level) is { } undefined)
            Set.Problems.Add(undefined);

        Set.Add(new DeclaredTrack<TInput, TReturn>(level.ToString(), null, then, null));
        return this;
    }

    /// <summary>
    /// Declares where the train goes when the score's confidence is below
    /// <see cref="RequireConfidence"/>. Without it, that fails the run.
    /// </summary>
    public ScaleTracks<TInput, TReturn, TLevel> Otherwise(
        Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> then
    )
    {
        Set.SetFallback("Otherwise", then);
        return this;
    }

    /// <summary>The confidence, from 0 to 1, below which the score is not followed. Defaults to 0.</summary>
    public ScaleTracks<TInput, TReturn, TLevel> RequireConfidence(double minimum)
    {
        if (TrackSet<TInput, TReturn>.ConfidenceProblem(minimum) is { } problem)
            Set.Problems.Add(problem);
        else
            MinimumConfidence = minimum;

        return this;
    }
}
