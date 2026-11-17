using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

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

    internal DeclaredTrack<TInput, TReturn>? YesTrack =>
        Set.Tracks.Find(t => string.Equals(t.Name, "Yes", StringComparison.Ordinal));

    internal DeclaredTrack<TInput, TReturn>? NoTrack =>
        Set.Tracks.Find(t => string.Equals(t.Name, "No", StringComparison.Ordinal));

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

        Set.Add(
            new DeclaredTrack<TInput, TReturn>(
                "Yes",
                Description: null,
                then,
                RequireConfidence: null
            )
        );
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

        Set.Add(
            new DeclaredTrack<TInput, TReturn>(
                "No",
                Description: null,
                then,
                RequireConfidence: null
            )
        );
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
