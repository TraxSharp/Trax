namespace Trax.Core.Decisions;

/// <summary>
/// The questions a decision step declares, as its decider will be asked them, without a state to
/// ask them about.
/// </summary>
/// <param name="Train">The train declaring them, named as <see cref="DecisionRequest.Train"/> names it.</param>
/// <param name="Step">The step that asks them, such as <c>Switch&lt;Order, Fulfilment&gt;</c>.</param>
/// <param name="State">The declared type of the state the questions are about.</param>
/// <param name="Questions">
/// The questions, all asked in one request when none of their answers is replayed.
/// </param>
public sealed record DeclaredQuestions(
    string Train,
    string Step,
    Type State,
    IReadOnlyList<Question> Questions
);
