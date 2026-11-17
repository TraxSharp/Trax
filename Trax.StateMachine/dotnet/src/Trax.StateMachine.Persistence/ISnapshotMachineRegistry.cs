using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Resolves the draft service (and exactly-once runner) for a machine by name. Populated once from the
/// discovered <see cref="IMachine"/>s; built per request over the scoped store/claims. This is what lets a
/// single generic mutation serve every registered machine via a <c>machine</c> discriminator.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotmachineregistry">SDK reference</seealso>
public interface ISnapshotMachineRegistry
{
    /// <summary>
    /// The draft service for the machine registered as <paramref name="machine"/>, or null if no machine has that
    /// name. Names match case-sensitively.
    /// </summary>
    /// <param name="machine">The machine id (<see cref="IMachine.Name"/>).</param>
    ISnapshotDraftService? Service(string machine);

    /// <summary>
    /// The exactly-once effect runner for the named machine, or null if the name is unknown or the machine binds
    /// no effect.
    /// </summary>
    /// <param name="machine">The machine id (<see cref="IMachine.Name"/>).</param>
    ISnapshotEffectRunner? EffectRunner(string machine);

    /// <summary>The registered machine's schema hash (<see cref="IMachine.SchemaHash"/>), or null if unknown.</summary>
    string? SchemaHash(string machine);
}
