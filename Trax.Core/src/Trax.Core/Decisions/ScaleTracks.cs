using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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

            if (
                !Set.Tracks.Exists(t =>
                    string.Equals(t.Name, lowest.ToString(), StringComparison.Ordinal)
                )
            )
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

        Set.Add(
            new DeclaredTrack<TInput, TReturn>(
                level.ToString(),
                Description: null,
                then,
                RequireConfidence: null
            )
        );
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
