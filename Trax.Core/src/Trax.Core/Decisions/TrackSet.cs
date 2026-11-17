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
        if (Tracks.Exists(t => string.Equals(t.Name, track.Name, StringComparison.Ordinal)))
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

        Fallback = new DeclaredTrack<TInput, TReturn>(
            name,
            Description: null,
            body,
            RequireConfidence: null
        );
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
