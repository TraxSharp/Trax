using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// The child machine a spawning state starts, as <see cref="IStateBuilder{TState,TTrigger}.Spawns{TChild,TContext,TOutput}"/>
/// declares it. The persistence layer resolves <see cref="ChildMachine"/> among the machines the host registers.
/// </summary>
/// <param name="ChildMachine">The child machine's class.</param>
/// <param name="ChildIdField">The parent context field the server writes the child's id into.</param>
/// <param name="CreateChildContext">Builds the child's initial context from the parent's, or null for the child's own.</param>
internal sealed record SpawnDefinition(
    Type ChildMachine,
    string ChildIdField,
    Func<JsonObject, JsonObject>? CreateChildContext
);
