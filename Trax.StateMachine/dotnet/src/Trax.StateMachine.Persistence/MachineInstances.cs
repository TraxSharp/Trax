using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>The default <see cref="IMachineInstances"/>, registered scoped by <c>AddStateMachines</c>.</summary>
internal sealed class MachineInstances(
    IEnumerable<IMachine> machines,
    IMachineInstanceStore store,
    InvokeOutbox outbox,
    IServiceProvider services
) : IMachineInstances
{
    public Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine =>
        Start(SystemMachine<TMachine>("start").Machine.Name, key, context, cancellationToken);

    public async Task<MachineInstance> Start(
        string machineId,
        MachineKey key,
        JsonObject? context = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var (machine, internals) = SystemMachine(machineId, "start");
        Keyed(machine, key, "start");
        var name = machine.Name;
        var id = MachineInstanceId.For(name, key);

        if (
            await store.Get(DraftOwner.System, name, id, cancellationToken).ConfigureAwait(false) is
            { } existing
        )
            return Existing(id, name, existing);

        var initial = internals.InitialSnapshot(context);

        // Entering an invoking initial state queues its run and sets the row's invoke token, in the transaction
        // that inserts the row, so a lost race to create the instance queues nothing.
        var created = await outbox
            .Insert(
                DraftOwner.System,
                id,
                initial,
                internals.Entering(initial.State),
                cancellationToken
            )
            .ConfigureAwait(false);
        switch (created)
        {
            case InvokeWrite.Written:
                return new MachineInstance(id, name, initial.State, Created: true);
            case InvokeWrite.Refused refused:
                throw new InvalidOperationException(
                    $"The system instance {id} of '{name}' could not be started ({refused.Code.ToWire()}): {refused.Message}",
                    refused.Exception
                );
        }

        // Another Start for the same key created the row between the read and the insert: return that one.
        return
            await store.Get(DraftOwner.System, name, id, cancellationToken).ConfigureAwait(false)
                is { } raced
            ? Existing(id, name, raced)
            : throw new InvalidOperationException(
                $"The system instance {id} of '{name}' could not be created, and none exists."
            );
    }

    public Task<AdvanceOutcome> Advance<TMachine>(
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine =>
        Advance(
            SystemMachine<TMachine>("advance").Machine.Name,
            key,
            trigger,
            input,
            cancellationToken
        );

    public Task<AdvanceOutcome> Advance(
        string machineId,
        MachineKey key,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var (machine, internals) = SystemMachine(machineId, "advance");
        Keyed(machine, key, "advance");
        return Advance(
            machine,
            internals,
            MachineInstanceId.For(machine.Name, key),
            trigger,
            input,
            cancellationToken
        );
    }

    public Task<AdvanceOutcome> Advance<TMachine>(
        Guid id,
        string trigger,
        JsonNode? input = null,
        CancellationToken cancellationToken = default
    )
        where TMachine : IMachine
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);

        var (machine, internals) = SystemMachine<TMachine>("advance");
        return Advance(machine, internals, id, trigger, input, cancellationToken);
    }

    private async Task<AdvanceOutcome> Advance(
        IMachine machine,
        IMachineInternals internals,
        Guid id,
        string trigger,
        JsonNode? input,
        CancellationToken cancellationToken
    )
    {
        var name = machine.Name;

        // Host code is trusted for rules, never for input: an oversized input is refused before anything is read,
        // and the engine checks the rest against the trigger's declared schema.
        if (SnapshotLimits.InputTooLarge(input))
            return new AdvanceOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The input exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        var stored = await store
            .Get(DraftOwner.System, name, id, cancellationToken)
            .ConfigureAwait(false);
        if (stored is null)
            return new AdvanceOutcome.NotFound();

        Snapshot current;
        switch (internals.Rehydrate(stored.Json))
        {
            case RehydrationResult.Ok ok:
                current = ok.Snapshot;
                break;
            case RehydrationResult.Error error:
                return new AdvanceOutcome.LoadError(error.Code, error.Message)
                {
                    Exception = error.Exception,
                };
            default:
                return new AdvanceOutcome.LoadError(
                    RefusalCode.Malformed,
                    "Unknown rehydration result."
                );
        }

        // The same reserved triggers a user's advance refuses: the system may move its instance, but only a run or an
        // effect records what it produced.
        if (internals.IsEffectBound(current.State, trigger))
            return new AdvanceOutcome.Rejected(
                RefusalCode.EffectBound,
                "This action runs an irreversible effect, which only its send fires."
            );
        if (internals.IsOutcomeTrigger(trigger))
            return new AdvanceOutcome.Rejected(
                RefusalCode.OutcomeBound,
                "This step is completed by the outcome of its work, not by an action."
            );

        Snapshot next;
        switch (internals.Advance(current, trigger, input))
        {
            case AdvanceResult.Rejected rejected:
                return new AdvanceOutcome.Rejected(rejected.Reason, rejected.Detail)
                {
                    Exception = rejected.Exception,
                };
            case AdvanceResult.Transitioned transitioned:
                next = transitioned.Snapshot;
                break;
            default:
                return new AdvanceOutcome.Rejected(RefusalCode.InternalError, Detail: null);
        }

        if (StorableJson.Problem(next.Context) is { } unstorable)
            return new AdvanceOutcome.Rejected(RefusalCode.Malformed, unstorable);
        if (Encoding.UTF8.GetByteCount(internals.Serialize(next)) > SnapshotLimits.MaxSnapshotBytes)
            return new AdvanceOutcome.Rejected(
                RefusalCode.TooLarge,
                $"The advanced snapshot exceeds the {SnapshotLimits.MaxSnapshotBytes}-byte limit."
            );

        // Leaving an invoking state cancels its run and entering one queues a new run, with the snapshot, in one
        // transaction. A self-loop neither leaves nor enters, and the state keeps its run.
        var moved = !string.Equals(next.State, current.State, StringComparison.Ordinal);
        var write = await outbox
            .Advance(
                DraftOwner.System,
                id,
                next,
                stored.Token,
                request: null,
                moved && internals.Entering(current.State) is not null ? stored.InvokeToken : null,
                moved ? internals.Entering(next.State) : null,
                cancellationToken,
                trigger
            )
            .ConfigureAwait(false);
        // A system instance another one spawned tells its parent when this advance ends it.
        if (write is InvokeWrite.Written && internals.Ends.ContainsKey(next.State))
            await SpawnedEnds
                .Notify(
                    services.GetRequiredService<IServiceScopeFactory>(),
                    DraftOwner.System,
                    machine.Name,
                    id,
                    cancellationToken
                )
                .ConfigureAwait(false);
        return write switch
        {
            InvokeWrite.Written written => new AdvanceOutcome.Advanced(next)
            {
                Revision = stored.Revision + 1,
                RowId = stored.RowId,
                DueAt = written.DueAt,
            },
            InvokeWrite.Refused refused => new AdvanceOutcome.Rejected(
                refused.Code,
                refused.Message
            )
            {
                Exception = refused.Exception,
            },
            _ => new AdvanceOutcome.Conflict(),
        };
    }

    // The registered, system-owned machine TMachine names, or why there is none.
    private (IMachine Machine, IMachineInternals Internals) SystemMachine<TMachine>(string action)
        where TMachine : IMachine
    {
        var machine =
            machines.OfType<TMachine>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{typeof(TMachine).Name} is not registered. Pass its assembly to AddStateMachines before "
                    + $"you {action} an instance of it."
            );
        return Checked(machine, typeof(TMachine).Name, action);
    }

    // The registered, system-owned machine whose id is machineId, or why there is none. An id no machine is
    // registered under is refused: host code never reaches an instance of a machine the host does not serve.
    private (IMachine Machine, IMachineInternals Internals) SystemMachine(
        string machineId,
        string action
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
        var machine =
            machines.FirstOrDefault(m => string.Equals(m.Name, machineId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No machine '{machineId}' is served, so the system cannot {action} an instance of it. Register "
                    + "it with AddStateMachines (its assembly, or ServeFeatures for a machine derived from features)."
            );
        return Checked(machine, $"'{machineId}'", action);
    }

    private static (IMachine Machine, IMachineInternals Internals) Checked(
        IMachine machine,
        string named,
        string action
    )
    {
        if (machine is not IMachineInternals internals)
            throw new InvalidOperationException(
                $"{named} does not derive from Machine<TState, TTrigger>, so the system cannot "
                    + $"{action} an instance of it."
            );
        if (!internals.SystemOwned)
            throw new InvalidOperationException(
                $"{named} ('{machine.Name}') is a user-owned machine, so the system cannot "
                    + $"{action} an instance of it. Declare SystemOwned() in its Configure (or system in its "
                    + "glossary) for its instances to belong to the system; its users then reach none of them."
            );
        return (machine, internals);
    }

    // A system machine served from features declares its key: one fixed key, or keyedBy parts. A key it never has names
    // an instance no view, policy or correctly keyed call reaches, so it is refused before anything is read or written.
    private static void Keyed(IMachine machine, MachineKey key, string action)
    {
        if (machine is IDeclaredSystemKey declared && declared.KeyProblem(key) is { } problem)
            throw new InvalidOperationException(
                $"'{machine.Name}' {problem}, so the system cannot {action} an instance of it at {key}."
            );
    }

    private static MachineInstance Existing(Guid id, string machine, StoredSnapshot stored) =>
        new(
            id,
            machine,
            JsonNode.Parse(stored.Json)?["state"]?.GetValue<string>() ?? string.Empty,
            Created: false
        );
}
