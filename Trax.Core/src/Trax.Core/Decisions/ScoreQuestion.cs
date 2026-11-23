namespace Trax.Core.Decisions;

/// <summary>
/// Place the state on <paramref name="Levels"/>, which run from lowest to highest. Answered with a
/// <see cref="ScoreAnswer"/>.
/// </summary>
public sealed record ScoreQuestion(string Key, string Instructions, IReadOnlyList<Criterion> Levels)
    : Question(Key, Instructions);
