---
title: "operations: machine instance mutations"
description: "Reference for cancelMachineInstance, the operations mutation that cancels a system-owned state machine instance's live train run."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 4
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.MachineInstances.cs
---

# operations: machine instance mutations

The field of `OperationsMutations` that acts on persisted state machine instances.

## cancelMachineInstance

Cancels a system-owned [state machine instance](/docs/statemachine/invoked-runs#operator-actions)'s
live train run, and the instance then moves through its state's `OnCancelled` edge. It is the
GraphQL counterpart of the Cancel button on the dashboard's
[instance page](/docs/dashboard/machines#an-instance); both call
[`IOperationsService.CancelMachineInstanceAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#state-machine-instances),
so they refuse the same instances with the same message.

A run still only queued is marked Cancelled and never starts. A run already dispatched has its
cancel requested: it stops at its next junction and ends Cancelled. The dispatcher claims an entry
and writes its run in one transaction, so a cancel racing the dispatcher ends exactly one of those
two ways. The outcome is then applied through the one conditional update every delivery makes,
so the instance moves once, whether this call, the run's lifecycle hook or the reconciler gets
there first.

Operators may cancel only `SYSTEM` instances. A user's draft is read-only to them: its run is
cancelled when the user leaves the state through one of the machine's own transitions, and
[`cancelExecution`](/docs/sdk-reference/graphql-api/mutations/executions#cancelexecution) and [`cancelWorkQueueEntry`](/docs/sdk-reference/graphql-api/mutations/work-queue#cancelworkqueueentry) refuse
that run and its queued entry too. No Trax-provided operation creates or advances a system
instance; a host can advance one from its own code through
[`IMachineInstances.Advance`](/docs/sdk-reference/statemachine-api/machine-instances) and expose
that under its own authorization.

```graphql
mutation {
  operations {
    cancelMachineInstance(machine: "fulfilment", ownerKind: SYSTEM, id: "6f9619ff-8b86-d011-b42d-00c04fc964ff") {
      success
      outcome
      message
      state
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `machine` | `String!` | Yes | The machine's id. Blank is refused with `TRAX_INVALID_ARGUMENT` |
| `ownerKind` | `SnapshotOwnerKind!` | Yes | Who owns the instance; only `SYSTEM` can be cancelled |
| `id` | `UUID!` | Yes | The instance id |

**Returns**: `MachineInstanceCancelResponse`: `success`, `outcome`, `message` (the dashboard shows
the same text) and `state`, the state the instance entered when this call moved it.

| `outcome` | `success` | Meaning |
|-----------|-----------|---------|
| `MOVED` | `true` | The queued run is cancelled, and this host, which registers the machine, moved the instance through `OnCancelled`; `state` is the state it entered |
| `RUN_CANCELLED` | `true` | The queued run is cancelled; a host that registers the machine moves the instance (on Postgres the cancel wakes its reconciler) |
| `CANCEL_REQUESTED` | `true` | The run was dispatched; it stops at its next junction, and the instance moves when it ends |
| `USER_OWNED` | `false` | A user's draft. Nothing changes |
| `NOT_FOUND` | `false` | No system instance has this machine and id |
| `NO_LIVE_RUN` | `false` | The instance's state waits on no run, so there is nothing to cancel |
| `RUN_ENDED` | `false` | The run has already ended; its own outcome is being applied |

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations: machine instance queries](/docs/sdk-reference/graphql-api/queries/machine-instances)
- [Invoking trains](/docs/statemachine/invoked-runs#operator-actions)
