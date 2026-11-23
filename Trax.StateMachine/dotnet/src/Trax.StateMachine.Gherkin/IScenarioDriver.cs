using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What a scenario runs against (ADR <c>Trax.Docs/adr/0058</c>): the derived machines in memory, or a host. The runner
/// reads each step's meaning and asks the driver, so a scenario means the same against both. A Given arranges the
/// instance directly (<see cref="Seed"/>), as a test arranges a database: it fires nothing and sets off no policy.
/// </summary>
internal interface IScenarioDriver
{
    /// <summary>
    /// A person the scenario introduced (<c>Given Ada is a customer</c>), before any step names them, so a Given that
    /// arranges their instance acts as their actor.
    /// </summary>
    void Introduce(Person person) { }

    ValueTask<Answer> Start(Person person, string machine);

    /// <summary>Puts an instance in a state with a context, starting it first if it has none.</summary>
    ValueTask Seed(InstanceKey key, string state, JsonObject? context);

    /// <summary>An instance's state and context, or null if it has none.</summary>
    ValueTask<(string State, JsonObject Context)?> Read(InstanceKey key);

    /// <summary>
    /// Marks the start of a When: <see cref="Cascaded"/> reports what the instances enter from here on.
    /// </summary>
    ValueTask BeginWhen();

    /// <summary>
    /// The states an instance entered since the current When began, in order: where the When took it, then where the
    /// policies it set off took it on. A Given's arranging is not a move and is not in it. Empty when it has not moved.
    /// </summary>
    ValueTask<IReadOnlyList<string>> Cascaded(InstanceKey key);

    /// <summary>Fires a trigger as <paramref name="person"/>, or as the server when that is null.</summary>
    ValueTask<Answer> Fire(Person? person, InstanceKey key, string trigger, JsonObject? input);

    /// <summary>Delivers an invoked train's outcome to the instance waiting in <paramref name="state"/>.</summary>
    ValueTask<Answer> Outcome(InstanceKey key, string state, string kind, JsonNode? output);

    ValueTask Pass(TimeSpan time);

    /// <summary>What one field of a view shows <paramref name="person"/>, or the refusal.</summary>
    ValueTask<(IReadOnlyList<string>? Values, Answer? Refused)> View(
        Person person,
        string view,
        string field
    );
}
