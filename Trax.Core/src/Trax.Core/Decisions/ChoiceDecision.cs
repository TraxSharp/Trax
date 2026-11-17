namespace Trax.Core.Decisions;

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
