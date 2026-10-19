---
title: "operations: machine queries"
description: "Reference for the operations queries over state machines: registered machines and their graphs, instances, one instance with its runs, and counts by state."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.MachineInstances.cs
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.Machines.cs
---

# operations: machine queries

Fields of `OperationsQueries` that read persisted state machines: the machines a host registers,
and their instances.

## machines

Every machine the host registers, ordered by name, whether or not it has any instances, with its
graph. It reads through `IOperationsService.GetMachinesAsync`, which reads each machine's IR by the
same reading [`trax machine show`](/docs/reference/cli) draws it from, so the two list the same
states and edges in the same order.

```graphql
query {
  operations {
    machines {
      name
      version
      systemOwned
      initialState
      states { name isInitial isFinal isCommitted invokesTrain spawnsMachine }
      transitions { from command to kind guard effect after }
      timers { state command target after field min max }
    }
  }
}
```

**Returns**: `[MachineDefinition!]!`.

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | The machine's id |
| `version` | `Int!` | The definition version it declares |
| `systemOwned` | `Boolean!` | True when only the system holds its instances |
| `schemaHash` | `String` | The hash of its IR, which each instance row records as the definition that last wrote it |
| `initialState` | `String` | The state an instance starts in |
| `states` | `[MachineDefinitionState!]!` | Every state in IR order: `name`, `isInitial`, `isFinal` (no transition leaves it), `isCommitted`, `invokesTrain` (the invoked train's interface full name) and `spawnsMachine` (the spawned machine's id) |
| `transitions` | `[MachineDefinitionTransition!]!` | Every edge: `from`, `command`, `to`, `kind` (`COMMAND`, `TIMER` or `OUTCOME`), `guard` (its message, or its rule's name), `effect` (the key prefix of the effect it runs once) and, for a timer, `after`. Commands come first, then timers, then each invoking state's outcomes (`Building.done`, `.failed`, `.cancelled`) in the order they are tried |
| `timers` | `[MachineDefinitionTimer!]!` | Every timed state's timer: `state`, `command`, `target`, and a fixed `after` or the context `field` with its `min` and `max`, all ISO-8601 durations |
| `graphUnavailableReason` | `String` | Why the machine has no graph: it was declared with C# delegates, which its IR cannot carry. Its states and transitions are then empty |

It carries what a machine declares and nothing about any instance: the counts and the instances are
the fields below.

## machineInstances

The instances of every [persisted state machine](/docs/statemachine) on the host, system-owned and
users' drafts alike, newest first by when each was last written. It reads through
`IOperationsService.GetMachineInstancesAsync` and `CountMachineInstancesAsync`, which the
operations console's [Machines pages](/docs/dashboard/machines) make.

```graphql
query {
  operations {
    machineInstances(machine: "fulfilment", state: "AwaitingPayment", ownerKind: SYSTEM, take: 25) {
      items {
        rowId
        machine
        ownerKind
        id
        state
        version
        createdAt
        updatedAt
        hasLiveInvokedRun
      }
      totalCount
      isCountCapped
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `machine` | `String` | `null` | Only instances of this machine, by its id, matched exactly |
| `state` | `String` | `null` | Only instances in this state, matched exactly |
| `ownerKind` | `SnapshotOwnerKind` | `null` | `SYSTEM` for instances created from code, `USER` for users' drafts; null for both |
| `skip` | `Int!` | `0` | Offset; above 10,000 is refused with `TRAX_SKIP_TOO_DEEP`, and the service the dashboard reads through refuses it too |
| `take` | `Int!` | `25` | Page size, from 1 to 500 |
| `stuck` | `Boolean` | `null` | `true` for only the [stuck](#needing-attention) instances, `false` for only the others; null for both |

**Returns**: `PagedResultOfMachineInstance!`. There is no keyset cursor (`nextCursor` is always
null), because the list is ordered by a time that moves as instances advance: page with `skip`. The
total counts at most 10,000 instances; past that it reads 10,000 with `isCountCapped` true, and
[`machineInstanceCounts`](#machineinstancecounts) has the exact numbers. At two million instances
each page, filtered or not, reads in a few milliseconds.

### MachineInstance fields

| Field | Type | Description |
|-------|------|-------------|
| `rowId` | `Long!` | The row's key. Pass it to [`machineInstance`](#machineinstance) to read a user's draft |
| `machine` | `String!` | The machine's id |
| `ownerKind` | `SnapshotOwnerKind!` | `SYSTEM` or `USER` |
| `id` | `UUID!` | The instance or draft id |
| `state` | `String!` | The state it is in |
| `version` | `Int!` | The machine definition version it was last written under |
| `createdAt` | `DateTime` | When it was created; null for one written before Trax recorded it |
| `updatedAt` | `DateTime!` | When it was last written |
| `hasLiveInvokedRun` | `Boolean!` | `true` while its state has [invoked a train](/docs/statemachine/invoking-trains) and waits for the run's outcome |
| `dueAt` | `DateTime` | When its state's timer fires; null when that state has no timer |
| `overdue` | `Boolean!` | `dueAt` is past by more than the host's timer poll, so the timer should have fired and has not |
| `waitingOn` | `MachineInstanceWaitingRun` | The run its state waits on: `executionId` (null while still queued), `trainState` (`PENDING` while queued, `CANCELLED` for an entry cancelled before it started) and `workQueueEntryId`. Null when it waits on none |
| `inStateSince` | `DateTime!` | When it entered its state, as far as Trax records it. No transition log is kept yet, so this is `updatedAt`, and a write that kept the state moves it: show it as "last changed" |
| `stuck` | `Boolean!` | It cannot move on its own; see [needing attention](#needing-attention) |

### Needing attention

An instance is **stuck** when its timer is `overdue`, or when the run it waits on failed or was
cancelled and its outcome was never applied: a run that ended more than one timer poll ago, or an
entry cancelled while still queued. The poll is the host's
`StateMachineOptions.InvokeOutcomeSweepInterval` (5 seconds by default), the pace at which the
reconciler fires timers and applies outcomes, so an instance is never stuck only because a sweep has
not come round yet. How long an instance has sat in its state is shown (`inStateSince`) and never
counted: a state some instances wait in for days is not a fault.

No field carries an instance's context, and none names the user who owns a draft. The context is an
untyped JSON object, so nothing could mask the sensitive parts of it; an operator sees where an
instance is, when it got there and which kind of owner holds it, never what it holds
([ADR 0046](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md)).
A user reaches their own drafts only through the `stateMachine` mutations, never through this list.

## machineInstance

One instance, or null when none matches. The owner kind is always named, because a user can hold a
draft under the same id as a system instance. A system instance is unique by machine and id. A
user's draft also needs its `rowId`, because several users can each hold a draft under one id and an
operator is not shown whose a draft is.

```graphql
query {
  operations {
    system: machineInstance(machine: "fulfilment", ownerKind: SYSTEM, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff") {
      state
      hasLiveInvokedRun
    }
    draft: machineInstance(machine: "checkout", ownerKind: USER, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff", rowId: 42) {
      state
      updatedAt
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `machine` | `String!` | none | The machine's id. Blank is refused with `TRAX_INVALID_ARGUMENT` |
| `ownerKind` | `SnapshotOwnerKind!` | none | Who owns the instance |
| `id` | `UUID!` | none | The instance or draft id |
| `rowId` | `Long` | `null` | The row's key from the list. Required for `USER` (refused with `TRAX_ROW_ID_REQUIRED` without it); for `SYSTEM`, when given, it must match too |

**Returns**: `MachineInstanceDetail`, with the fields of [`MachineInstance`](#machineinstance-fields)
and the train runs the instance [invoked](/docs/statemachine/invoking-trains), which only this
lookup reads:

| Field | Type | Description |
|-------|------|-------------|
| `invokedRuns` | `[MachineInstanceInvokedRun!]!` | The runs, newest first, at most 50: `id`, `externalId`, `name` (the train), `trainState`, `startTime`, `endTime`, `failureClass`, `cancellationRequested`, and `isLive` for the run the instance's state waits on. Never a run's input or output; [`executionDetail(id)`](/docs/sdk-reference/graphql-api/queries/executions#executiondetail) reads the rest, masked |
| `isInvokedRunsCapped` | `Boolean!` | True when the instance invoked more than 50 runs |
| `queuedInvokedRunEntryId` | `Long` | The work queue entry of the run the state waits on while it is still queued, and so not yet a run |

A system instance lists every run it invoked. A user's draft lists only its live run: a run
records the machine, instance id and owner kind that queued it, not the user, and several users
can each hold a draft under one id, so listing every run under the id could show another user's.

```graphql
query {
  operations {
    machineInstance(machine: "fulfilment", ownerKind: SYSTEM, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff") {
      state
      invokedRuns { id name trainState startTime endTime failureClass isLive }
      queuedInvokedRunEntryId
    }
  }
}
```

The one operator action on an instance is
[`cancelMachineInstance`](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance).

### transitions

The instance's history, newest first: one `MachineInstanceTransition` for every write of its snapshot Trax recorded,
its creation, each command, save, timer and run outcome. It reads through
`IOperationsService.GetMachineInstanceTransitionsAsync`, the history the console's instance page shows, and only when
selected. The history is the row's (`rowId`), so a user's draft shows only its own, and it starts with the first write
after the upgrade that added it ([089](/docs/reference/schema-migrations#state-machine-instance-history-089-sqlite-048)).

```graphql
query {
  operations {
    machineInstance(machine: "fulfilment", ownerKind: SYSTEM, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff") {
      transitions(first: 25) {
        items {
          revision kind trigger actor fromState toState at previousAt
          startedRun { executionId trainState workQueueEntryId }
          startedChild { machine ownerKind id rowId }
        }
        nextCursor
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `first` | `Int!` | `25` | The page size, clamped to 1 through 500 |
| `after` | `Long` | `null` | Only transitions older than this revision: the previous page's `nextCursor` |

**Returns**: `MachineInstanceTransitions!`: `items`, the transitions on this page, and `nextCursor`, the revision to
pass as `after` for the next page, or null on the last. Each `MachineInstanceTransition` has:

| Field | Type | Description |
|-------|------|-------------|
| `revision` | `Long!` | The revision the write gave the instance. A write that left the snapshot alone, or one made around Trax's store, has none, so revisions can skip |
| `kind` | `SnapshotTransitionKind!` | `START` (created: a start, a save that created a draft, or a parent spawning it), `COMMAND` (an event moved it, or an effect it bound ran), `SAVE` (an autosave, or host code writing the snapshot), `TIMER` (its state's timer fired) or `OUTCOME` (a run it invoked ended) |
| `trigger` | `String` | The command: the event's name, the timer's event, or `State.done`, `State.failed` or `State.cancelled` for an outcome. Null for a creation, a save, and an outcome that could not be applied, which leaves the instance where it was |
| `actor` | `SnapshotTransitionActor!` | `OWNER` (a client acting for a user's instance), `HOST` (host code driving a system instance) or `MACHINE` (a timer, an outcome, a spawn). Never which user |
| `fromState` | `String` | The state it left; null for its creation |
| `toState` | `String!` | The state it entered |
| `at` | `DateTime!` | When it was written |
| `previousAt` | `DateTime` | When the transition before it was written, so `at` minus `previousAt` is the time spent in `fromState` since then; null for the oldest |
| `startedRun` | `MachineInstanceTransitionRun` | The run entering `toState` queued: `executionId` (null while queued), `trainState` (`PENDING` while queued, `CANCELLED` if its entry was cancelled first) and `workQueueEntryId`. Never its token |
| `startedChild` | `MachineInstanceTransitionChild` | The [child](/docs/statemachine/spawning-child-machines) it spawned: `machine`, `ownerKind`, `id` and `rowId`, as `machineInstance` takes them |

No transition carries the instance's context or its owner's key. The history is deleted with the instance.

## machineInstanceCounts

How many instances each machine has in each state, for each owner kind, ordered by machine, state
and owner kind. Exact. It reads through `IOperationsService.GetMachineInstanceStateCountsAsync`,
the counts the dashboard's State Machines page shows.

```graphql
query {
  operations {
    machineInstanceCounts(machine: "fulfilment") { machine state ownerKind count }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `machine` | `String` | `null` | Only this machine's counts; null for every machine |

**Returns**: `[MachineInstanceCount!]!`, each with `machine: String!`, `state: String!`,
`ownerKind: SnapshotOwnerKind!`, `count: Long!` and `stuck: Long!`, how many of them are
[stuck](#needing-attention). On Postgres the counts are read from the
listing's index alone: about 120 ms over two million instances. Each host keeps the counts it read
for 5 seconds, per `machine` filter, and callers asking meanwhile share that read, so every
dashboard and API client polling them costs one read, not one each; a count can be that old.

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: machine instance mutations](/docs/sdk-reference/graphql-api/mutations/machine-instances)
- [Invoking trains](/docs/statemachine/invoking-trains)
