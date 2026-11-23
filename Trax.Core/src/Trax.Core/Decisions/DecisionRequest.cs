namespace Trax.Core.Decisions;

/// <summary>
/// The questions a train asks about one state.
/// </summary>
/// <param name="Train">The name of the train asking, for logging.</param>
/// <param name="State">
/// The value the questions are about. The live decider is handed the instance in the run's Memory,
/// not a copy, and reads it without changing it. A shadow is handed a copy of its own, made through
/// JSON, so nothing it does to it reaches the run or another shadow.
/// </param>
/// <param name="Questions">The questions, each with a key unique within the request.</param>
public sealed record DecisionRequest(string Train, object State, IReadOnlyList<Question> Questions);
