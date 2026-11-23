namespace Trax.Core.Decisions;

/// <summary>
/// The probability of yes. There is no separate confidence: the probability is the answer.
/// </summary>
public sealed record YesNoAnswer(double Probability) : Answer;
