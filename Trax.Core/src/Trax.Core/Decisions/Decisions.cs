namespace Trax.Core.Decisions;

/// <summary>
/// The question a choice, scale or yes/no type asks, written once on the type.
/// </summary>
/// <remarks>
/// Put it on the enum a <c>Switch</c> or <c>Scale</c> routes on, or on the marker type a
/// <c>Gate</c> routes on. A model sees only the question and the criteria, never the type's name,
/// so the question has to say everything that is being judged. An <c>asking:</c> argument where
/// the decision is declared overrides it.
/// </remarks>
[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class AsksAttribute(string question) : Attribute
{
    /// <summary>What is being asked.</summary>
    public string Question { get; } = question;

    /// <summary>For a yes/no question, what a yes means.</summary>
    public string? Yes { get; init; }

    /// <summary>For a yes/no question, what a no means.</summary>
    public string? No { get; init; }

    /// <summary>
    /// The key the question is asked under, in place of the type's name: 1 to
    /// <see cref="QuestionKey.MaxLength"/> ASCII letters, digits, <c>_</c>, <c>-</c> or <c>.</c>,
    /// such as <c>refund_choice</c>.
    /// </summary>
    /// <remarks>
    /// The key identifies the question to a decider, to a model's adapter, and to a replay of an
    /// earlier run. Set it to keep the key a type had before it was renamed, so a requeued run
    /// still replays what the original answered, or to tell apart two types in one train that
    /// share a name. See <see cref="QuestionKey"/>.
    /// </remarks>
    public string? Key { get; init; }
}

/// <summary>
/// A choice between the members of <typeparamref name="TTrack"/>, put in Memory when it is decided.
/// </summary>
/// <param name="Choice">The chosen member.</param>
/// <param name="Confidence">How sure the decider was, from 0 to 1.</param>
/// <param name="Probabilities">Each member's probability, when the decider gave them.</param>
/// <param name="Model">The model that decided, as the decider named it, or null for one that is not a model.</param>
public sealed record ChoiceDecision<TTrack>(
    TTrack Choice,
    double Confidence,
    IReadOnlyDictionary<TTrack, double>? Probabilities,
    string? Model
)
    where TTrack : struct, Enum;

/// <summary>
/// A position on the ordered levels of <typeparamref name="TLevel"/>, put in Memory when it is
/// decided. The levels are the enum's members in order of their values, lowest first.
/// </summary>
/// <param name="Score">The position, from 0 (the lowest level), which may fall between levels.</param>
/// <param name="Nearest">The level the score rounds to.</param>
/// <param name="Confidence">How sure the decider was, from 0 to 1.</param>
/// <param name="Probabilities">Each level's probability, when the decider gave them.</param>
/// <param name="Model">The model that decided, as the decider named it, or null for one that is not a model.</param>
public sealed record ScoreDecision<TLevel>(
    double Score,
    TLevel Nearest,
    double Confidence,
    IReadOnlyDictionary<TLevel, double>? Probabilities,
    string? Model
)
    where TLevel : struct, Enum;

/// <summary>
/// The probability of yes to the question <typeparamref name="TQuestion"/> asks, put in Memory
/// when it is decided.
/// </summary>
/// <typeparam name="TQuestion">
/// A marker type naming the question, usually carrying <see cref="AsksAttribute"/>. Memory is keyed
/// by type, so each yes/no question a train asks needs its own.
/// </typeparam>
/// <param name="Probability">The probability of yes, from 0 to 1.</param>
/// <param name="Model">The model that decided, as the decider named it, or null for one that is not a model.</param>
public sealed record YesNoDecision<TQuestion>(double Probability, string? Model);

/// <summary>
/// Which track a <c>Switch</c>, <c>Gate</c> or <c>Scale</c> sent the train down, put in Memory
/// before the track runs.
/// </summary>
/// <typeparam name="TKey">The enum or marker type the routing was on.</typeparam>
/// <param name="Track">The track's name: an enum member, <c>Yes</c>, <c>No</c>, <c>Unsure</c> or <c>Otherwise</c>.</param>
/// <param name="FallbackReason">Why the decision was not followed, or null when it was.</param>
public sealed record TrackTaken<TKey>(string Track, string? FallbackReason)
{
    /// <summary>True when the decision was not followed and a fallback track was taken.</summary>
    public bool TookFallback => FallbackReason is not null;
}
