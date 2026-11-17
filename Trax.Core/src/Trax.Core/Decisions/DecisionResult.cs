namespace Trax.Core.Decisions;

/// <summary>
/// A decider's answers.
/// </summary>
/// <param name="Answers">One answer per question, keyed by <see cref="Question.Key"/>.</param>
public sealed record DecisionResult(IReadOnlyDictionary<string, Answer> Answers);
