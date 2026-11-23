namespace Trax.Core.Decisions;

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
