namespace Trax.Core.Decisions;

/// <summary>
/// A shadow decider's answer, which the run never acts on.
/// </summary>
/// <param name="Decider">The shadow decider.</param>
/// <param name="Answer">Its answer, or null when it failed or did not answer in time.</param>
/// <param name="Agrees">
/// Whether its answer would have done what the live answer did. When the step that asked also
/// routes (<c>Switch</c>, <c>Gate</c> or <c>Scale</c> with a state), that is taking the same
/// track by the step's own bars and bands. For a plain <c>Decide</c>, whose routing comes later,
/// it is the same option, the same nearest level, or the same side of one half for a yes/no.
/// False when it did not answer, answered with something that does not fit the question, or when
/// the step had no track to take on the live answer.
/// </param>
/// <param name="Error">Why it gave no answer, or null when it did.</param>
public sealed record ShadowAnswer(Type Decider, Answer? Answer, bool Agrees, string? Error);
