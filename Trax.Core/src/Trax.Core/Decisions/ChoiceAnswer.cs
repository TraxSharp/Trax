namespace Trax.Core.Decisions;

/// <summary>
/// The chosen option, by <see cref="Criterion.Name"/>.
/// </summary>
/// <param name="Choice">The chosen option's name.</param>
/// <param name="Confidence">How sure the decider is, from 0 to 1.</param>
/// <param name="Probabilities">Each option's probability, by name, when the decider has them.</param>
public sealed record ChoiceAnswer(
    string Choice,
    double Confidence = 1.0,
    IReadOnlyDictionary<string, double>? Probabilities = null
) : Answer;
