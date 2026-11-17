namespace Trax.Core.Decisions;

/// <summary>
/// Pick one of <paramref name="Options"/>. Answered with a <see cref="ChoiceAnswer"/>.
/// </summary>
public sealed record ChoiceQuestion(
    string Key,
    string Instructions,
    IReadOnlyList<Criterion> Options
) : Question(Key, Instructions);
