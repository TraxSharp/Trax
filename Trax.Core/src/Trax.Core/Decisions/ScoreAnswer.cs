namespace Trax.Core.Decisions;

/// <summary>
/// A position on the levels, from 0 (the first level) to one less than the number of levels.
/// </summary>
/// <param name="Score">The position, which may fall between levels.</param>
/// <param name="Confidence">How sure the decider is, from 0 to 1.</param>
/// <param name="Probabilities">Each level's probability, in level order, when the decider has them.</param>
public sealed record ScoreAnswer(
    double Score,
    double Confidence = 1.0,
    IReadOnlyList<double>? Probabilities = null
) : Answer;
