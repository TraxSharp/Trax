using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The C# handler of a custom rule a machine served from its IR names, the one
/// <see cref="IMachineBuilder{TState,TTrigger}.CustomGuard"/> binds on a machine written in C#. A host file names its
/// type by full name, and the host creates one with its public parameterless constructor when it builds the machine,
/// so it holds no state and reaches no service: it answers from the context and the input alone.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public interface ICustomRule
{
    /// <summary>Whether the rule holds of <paramref name="context"/> and the trigger's <paramref name="input"/>.</summary>
    /// <param name="context">The snapshot's context; it is not modified.</param>
    /// <param name="input">The trigger's input, or null when it has none.</param>
    bool Holds(JsonObject context, JsonNode? input);
}
