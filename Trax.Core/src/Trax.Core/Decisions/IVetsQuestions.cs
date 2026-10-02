namespace Trax.Core.Decisions;

/// <summary>
/// A decider that can tell from the declaration alone that it cannot answer a question a train
/// will put to it: more or fewer options than its model takes, a question with no words, a state
/// it cannot send.
/// </summary>
/// <remarks>
/// When a train's chain is read (<c>DeclaredChain</c>, which the startup check reads every
/// registered train with), each decider a decision step names, the live one and each shadow, is
/// looked up the way the run finds it: among the services handed to <c>AddServices</c>, then in the
/// train's container. One that implements this is asked about what the step declares, and each
/// problem it names refuses the chain, so a host does not start with a decision that would be
/// refused on every run. A decider that cannot be built outside a request is not vetted, and still
/// refuses at run time whatever it cannot answer. <see cref="CascadingDecider"/> vets with each of
/// its tiers.
/// </remarks>
public interface IVetsQuestions
{
    /// <summary>
    /// What this decider cannot do with <paramref name="declared"/>, each phrased for whoever has
    /// to fix it, or nothing when it can answer.
    /// </summary>
    IEnumerable<string> Problems(DeclaredQuestions declared);
}

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
