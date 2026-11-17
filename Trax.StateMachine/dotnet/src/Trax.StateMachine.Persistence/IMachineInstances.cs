using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// Creates the instances of a machine that the system owns rather than a user: one per partition of a source, say,
/// each driven by the trains its states invoke. They are created only from code (a train, or startup); no GraphQL
/// operation creates, reads or advances one, and no user's request reaches one, even under the same id.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine-instances">SDK reference</seealso>
public interface IMachineInstances
{
    /// <summary>
    /// Creates the system-owned instance of <typeparamref name="TMachine"/> for <paramref name="key"/> in the
    /// machine's initial state with <paramref name="context"/>, or returns the one that already exists. The id is
    /// derived from the machine's name and the key (<see cref="MachineInstanceId.For"/>), so calling this twice, or
    /// from two hosts at once, finds one instance: the second call creates nothing and returns the first's.
    /// </summary>
    /// <typeparam name="TMachine">A machine registered through <c>AddStateMachines</c>.</typeparam>
    /// <param name="key">What the instance is for: one or more parts, such as a source and a partition.</param>
    /// <param name="context">
    /// The initial context, validated against the initial state like any snapshot. Null uses the context the
    /// machine declares with <c>StartsAt</c>. Ignored when the instance already exists.
    /// </param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>The instance: its id, its machine, the state it is in now, and whether this call created it.</returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMachine"/> is not registered, does not declare <c>SystemOwned()</c>, or its initial state
    /// invokes a train whose run could not be queued.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="context"/> is not a valid context for the initial state, or makes the snapshot larger than
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/>.
    /// </exception>
    Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine;

    /// <summary>
    /// Fires <paramref name="trigger"/> on the system-owned instance of <typeparamref name="TMachine"/> for
    /// <paramref name="key"/>, as the system: the code path through which a system instance moves other than by its
    /// trains' outcomes, such as a <c>Retry</c> out of a failure state. It is checked exactly as a user's advance is:
    /// the transition's guard and the target state's context rule, the 64 KiB cap, and the reserved triggers, so an
    /// invoked train's outcome trigger is refused as <c>OUTCOME_BOUND</c> and an effect-bound one as
    /// <c>EFFECT_BOUND</c>. Entering or leaving an invoking state queues or cancels the run in the same transaction,
    /// as an advance does; the run is authorized in the trusted scope, as every run of a system-owned machine is.
    /// </summary>
    /// <remarks>
    /// No GraphQL operation calls this; a host that lets a person retry a system instance writes its own operation
    /// over it, under its own authorization.
    /// </remarks>
    /// <typeparam name="TMachine">A system-owned machine registered through <c>AddStateMachines</c>.</typeparam>
    /// <param name="key">The instance's key, as it was started with.</param>
    /// <param name="trigger">The trigger to fire, by name.</param>
    /// <param name="input">The trigger's input, when it takes one.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>
    /// <see cref="AdvanceOutcome.Advanced"/> with the new snapshot; <see cref="AdvanceOutcome.NotFound"/> when no
    /// instance exists for the key; <see cref="AdvanceOutcome.Rejected"/> with the engine's or the outbox's reason;
    /// <see cref="AdvanceOutcome.LoadError"/> when the stored snapshot cannot be read; or
    /// <see cref="AdvanceOutcome.Conflict"/> when the instance changed while this call ran (an outcome landed, say).
    /// Nothing is written unless it is <see cref="AdvanceOutcome.Advanced"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TMachine"/> is not registered or does not declare <c>SystemOwned()</c>.
    /// </exception>
    Task<AdvanceOutcome> Advance<TMachine>(
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine;

    /// <summary>
    /// <see cref="Advance{TMachine}(MachineKey, string, JsonNode?, CancellationToken)"/> on the system-owned instance
    /// <paramref name="id"/> names, for an instance that has no key: a child another system instance's state spawned,
    /// whose id its parent's context holds. An advance that puts the child in a state it ends in gives its parent the
    /// outcome. A service that does not implement it throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <typeparam name="TMachine">A system-owned machine registered through <c>AddStateMachines</c>.</typeparam>
    /// <param name="id">The instance's id.</param>
    /// <param name="trigger">The trigger to fire, by name.</param>
    /// <param name="input">The trigger's input, when it takes one.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    Task<AdvanceOutcome> Advance<TMachine>(
        Guid id,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine =>
        throw new NotSupportedException($"{GetType().Name} cannot advance an instance by its id.");

    /// <summary>
    /// <see cref="Start{TMachine}"/> for the machine whose id is <paramref name="machineId"/>, for a machine with no C#
    /// type of its own: one served from its IR (<c>ServeFeatures</c>), whose type is emitted when the host starts. The
    /// typed overload calls this. A service that does not implement it throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="machineId">The machine's id (<see cref="IMachine.Name"/>), as its IR and the host file name it.</param>
    /// <param name="key">What the instance is for: one or more parts, such as a source and a partition.</param>
    /// <param name="context">The initial context, or null for the machine's declared one. Ignored when the instance exists.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>The instance: its id, its machine, the state it is in now, and whether this call created it.</returns>
    /// <exception cref="InvalidOperationException">
    /// No registered machine has the id <paramref name="machineId"/>, it is not system-owned, its initial state
    /// invokes a train whose run could not be queued, or it is served from features and <paramref name="key"/> is not
    /// one of its keys: a keyed system machine's key has one part per <c>keyedBy</c> name, and a system machine at a
    /// fixed key has that one instance.
    /// </exception>
    Task<MachineInstance> Start(
        string machineId,
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException($"{GetType().Name} cannot start a machine by its id.");

    /// <summary>
    /// <see cref="Advance{TMachine}(MachineKey, string, JsonNode?, CancellationToken)"/> for the machine whose id is
    /// <paramref name="machineId"/>, for a machine served from its IR. A trigger only the server fires (a policy's) is
    /// fired here like any other: this is the server's call, never a client's. The typed overload calls this. A
    /// service that does not implement it throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="machineId">The machine's id (<see cref="IMachine.Name"/>).</param>
    /// <param name="key">The instance's key, as it was started with.</param>
    /// <param name="trigger">The trigger to fire, by name.</param>
    /// <param name="input">The trigger's input, when it takes one.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    /// <returns>As the typed overload returns.</returns>
    /// <exception cref="InvalidOperationException">
    /// No registered machine has the id <paramref name="machineId"/>, it is not system-owned, or <paramref name="key"/>
    /// is not one of its keys, as <see cref="Start(string, MachineKey, JsonObject?, CancellationToken)"/> refuses it.
    /// </exception>
    Task<AdvanceOutcome> Advance(
        string machineId,
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException($"{GetType().Name} cannot advance a machine by its id.");
}
