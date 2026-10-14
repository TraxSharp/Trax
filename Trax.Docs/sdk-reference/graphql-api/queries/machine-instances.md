---
title: "operations: machine instance queries"
description: "Reference for the operations queries over persisted state machine instances: the paged list, one instance with its invoked runs, and counts by state."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.MachineInstances.cs
---

# operations: machine instance queries

Fields of `OperationsQueries` that read the instances of persisted state machines.

## machineInstances

The instances of every [persisted state machine](/docs/statemachine) on the host, system-owned and
users' drafts alike, newest first by when each was last written. It reads through
`IOperationsService.GetMachineInstancesAsync` and `CountMachineInstancesAsync`, the reads the
dashboard's [State Machines page](/docs/dashboard/pages#state-machines) makes.

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
`ownerKind: SnapshotOwnerKind!` and `count: Long!`. On Postgres the counts are read from the
listing's index alone: about 120 ms over two million instances. Each host keeps the counts it read
for 5 seconds, per `machine` filter, and callers asking meanwhile share that read, so every
dashboard and API client polling them costs one read, not one each; a count can be that old.

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: machine instance mutations](/docs/sdk-reference/graphql-api/mutations/machine-instances)
- [Invoking trains](/docs/statemachine/invoking-trains)
