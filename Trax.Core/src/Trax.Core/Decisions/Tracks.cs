using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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
/// <seealso href="https://traxsharp.net/docs/sdk-reference/train-methods/switch">SDK reference</seealso>
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
        var chosen = Set.Tracks.Find(t =>
            string.Equals(t.Name, decision.Choice.ToString(), StringComparison.Ordinal)
        );
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
