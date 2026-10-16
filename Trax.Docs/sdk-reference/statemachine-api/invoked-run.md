---
title: InvokedRun
description: Reference for InvokedRun, which a junction of a train a machine invokes injects to learn the instance that queued the run and the user who owns it.
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 11
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/InvokedRun.cs
---

# InvokedRun

The machine instance whose invoking state queued the run going on, and who owns it. A train a machine invokes has no caller of its own (it runs on the scheduler, where `TraxPrincipal` cannot be injected), and its input is built from the machine's context alone, so this is how one of its junctions learns which user it acts for. `AddScheduler`, `AddTraxJobRunner` and `AddStateMachines` register it.

## Signature

```csharp fragment
namespace Trax.Effect.StateMachine.Persistence;

public sealed record InvokedRun(
    string Machine,
    Guid InstanceId,
    SnapshotOwnerKind OwnerKind,
    string? UserKey
);
```

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Machine` | `string` | The id of the machine whose state queued the run |
| `InstanceId` | `Guid` | The instance that queued the run |
| `OwnerKind` | `SnapshotOwnerKind` | `User` or `System` |
| `UserKey` | `string?` | The owning user's key; `null` for a system-owned instance |

## Behavior

- The job runner resolves it when the run starts, from the run's record and the instance's row. The run names the instance that queued it, and the instance names its owner. The run stores only a hash of the owner's key, never the key, and only an owner whose key matches that hash is taken, so another user holding an instance with the same id is never mistaken for the owner.
- It is resolved for every run on the flow the job runner starts, whichever runner executes it: a local worker, a remote worker or Lambda. A train a junction of the invoked train runs on the same flow sees the same value.
- **It fails closed.** Resolving it throws `InvalidOperationException` in a run no machine invoked (a train run directly, queued by a caller, or scheduled by a manifest), and in an invoked run whose owner cannot be told: the instance was deleted while the run waited, or its owner no longer matches the run. Inject it only into junctions of trains a machine invokes; a junction that also runs on other paths takes the owner from its input instead.
- It is registered transient and read from the current run each time, so a junction may be registered with any lifetime.

## Example

```csharp compile
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.StateMachine.Persistence;

public record PlayCard(Guid GameId, int Slot);

public record CardPlayed(Guid GameId);

public interface IGames
{
    Task<string> OwnerOf(Guid gameId);
}

public class PlayCardJunction(InvokedRun invokedBy, IGames games)
    : EffectJunction<PlayCard, CardPlayed>
{
    public override async Task<CardPlayed> Run(PlayCard input)
    {
        // The game id came from the machine's context; the player is the instance's owner.
        if (await games.OwnerOf(input.GameId) != invokedBy.UserKey)
            throw new TrainException("That game is not yours.");

        return new CardPlayed(input.GameId);
    }
}
```

## See also

- [Invoke a train from a state: who a run belongs to](/docs/statemachine/invoking-trains#who-a-run-belongs-to)
- [Inject the current principal](/docs/security/inject-the-current-principal)
- [Invoked runs](/docs/statemachine/invoked-runs)
