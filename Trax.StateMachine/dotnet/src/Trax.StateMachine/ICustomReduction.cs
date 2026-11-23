using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The C# handler of a custom reduction a machine served from its IR names, the one
/// <see cref="IMachineBuilder{TState,TTrigger}.CustomReducer"/> binds on a machine written in C#. Created as an
/// <see cref="ICustomRule"/> is.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public interface ICustomReduction
{
    /// <summary>The destination context, from the current one and the trigger's input.</summary>
    /// <param name="context">The current context; return a new object rather than changing it.</param>
    /// <param name="input">The trigger's input, or null when it has none.</param>
    JsonObject Reduce(JsonObject context, JsonNode? input);
}
