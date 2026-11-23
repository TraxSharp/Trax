using System.Text.Json.Nodes;

namespace Trax.StateMachine;

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
