namespace Trax.Core.Decisions;

/// <summary>
/// Yes or no, answered with the probability of yes in a <see cref="YesNoAnswer"/>.
/// </summary>
/// <param name="Key">Identifies the question in the answers.</param>
/// <param name="Instructions">What is being asked.</param>
/// <param name="Yes">What a yes means, when the question alone does not say.</param>
/// <param name="No">What a no means, when the question alone does not say.</param>
public sealed record YesNoQuestion(string Key, string Instructions, string? Yes, string? No)
    : Question(Key, Instructions);
