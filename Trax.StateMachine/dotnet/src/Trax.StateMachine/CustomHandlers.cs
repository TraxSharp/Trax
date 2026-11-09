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

/// <summary>
/// A forward migration of a machine served from its IR, from one version to the next, the one
/// <see cref="IMachineBuilder{TState,TTrigger}.MigrateFrom"/> registers on a machine written in C#. A host file names
/// one per version step, and the host creates it as it creates an <see cref="ICustomRule"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-machines">SDK reference</seealso>
public interface IMachineMigration
{
    /// <summary>The snapshot's state and context at the next version, from its state and context at this one.</summary>
    /// <param name="state">The stored snapshot's state.</param>
    /// <param name="context">The stored snapshot's context; return a new object rather than changing it.</param>
    MigrationResult Migrate(string state, JsonObject context);
}
