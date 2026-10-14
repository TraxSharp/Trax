---
title: "operations: manifest mutations"
description: "Reference for the operations mutations on manifests and groups: trigger, delay, enable, disable, cancel, batch variants and updateManifest."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.cs
---

# operations: manifest mutations

Fields of `OperationsMutations`, the type behind `mutation { operations { ... } }`, that act on manifests and manifest groups. Each returns [`OperationResponse`](/docs/sdk-reference/graphql-api/mutations/operation-response) unless it says otherwise.

The five mutations that take a manifest's `externalId` (`triggerManifest`, `triggerManifestDelayed`, `disableManifest`, `enableManifest`, `cancelManifest`) answer an id no manifest has with `success: false` and the message `Manifest '<externalId>' not found.`, and change nothing. It is a refusal, not a GraphQL error.

## triggerManifest

Triggers an immediate execution of a manifest, bypassing its normal schedule. A manifest holds at most one queued work queue entry, so when it already has one, nothing more is queued and that entry becomes the triggered run: it runs even if the manifest is disabled, and an entry due later (a retry waiting out its backoff) is brought forward to now. It goes through `IOperationsService.TriggerManifestAsync`, the call the dashboard's **Run Now** makes, and its message says which happened, naming the work queue entry, whose id is the response's `id`:

- `Manifest 'order-processing-daily' triggered: queued a new run (work queue entry 42), due now.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42); the trigger brought it forward, now due now, and queued nothing more.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42), due now; it now runs as the trigger and nothing more was queued.`
- `Manifest 'order-processing-daily' already had a queued run (work queue entry 42) that the dispatcher claimed as the trigger reached it, so it is already running; nothing more was queued.`

On a host whose store is in memory (`UseInMemory()` and no database provider) nothing dispatches the work queue, so the trigger answers `success: false` with that reason and queues nothing; the same holds for `triggerManifestDelayed`, `triggerGroup`, `triggerManifests`, `triggerGroups`, `workQueue.queueTrain`, `requeueExecution`, and the dead-letter requeues (`requeueDeadLetter`, `requeueDeadLetters`, `requeueAllDeadLetters`) ([Trax.Scheduler ADR 0019](https://github.com/TraxSharp/Trax/blob/main/Trax.Scheduler/docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md)).

```graphql
mutation {
  operations {
    triggerManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |
| `askAfresh` | `Boolean!` | No | Default `false`. When `true` and the trigger releases a queued retry that would [replay the failed run's decisions](/docs/scheduler/how-retries-work#retries-replay-decisions), the run asks its deciders afresh instead |

**Returns**: `OperationResponse`. With `askAfresh: true`, a retry the dispatcher claimed before the
trigger reached it can no longer be changed. The mutation still succeeds, and its message says so:
`"Manifest 'order-processing-daily' triggered, but the dispatcher had already claimed its queued retry (work queue entry 42), so that run replays the decisions of execution 41 rather than asking its deciders afresh."`.

## triggerManifestDelayed

Triggers a manifest execution after a specified delay. When the manifest already has a queued entry, no second one is queued: that entry keeps its time if it is due sooner, and is brought forward to now plus `delay` otherwise. As with `triggerManifest`, the message says which happened and when the run is due, and `id` is the work queue entry.

```graphql
mutation {
  operations {
    triggerManifestDelayed(
      externalId: "order-processing-daily"
      delay: "PT5M"
    ) {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |
| `delay` | `TimeSpan!` | Yes | How long to wait before triggering, as an ISO-8601 duration (e.g. `"PT5M"` for 5 minutes) |
| `askAfresh` | `Boolean!` | No | Default `false`. As on `triggerManifest`, including the message when the dispatcher claimed the retry first |

**Returns**: `OperationResponse`

## disableManifest

Disables a manifest. Disabled manifests are skipped during scheduling cycles.

```graphql
mutation {
  operations {
    disableManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse`

## enableManifest

Re-enables a previously disabled manifest.

```graphql
mutation {
  operations {
    enableManifest(externalId: "order-processing-daily") {
      success
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse`

## cancelManifest

Requests cancellation of every pending and running execution of a manifest. Sets `CancellationRequested` on each, which the run observes at its next junction boundary on any host; a run on the host that received the mutation is also cancelled at once.

```graphql
mutation {
  operations {
    cancelManifest(externalId: "order-processing-daily") {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `externalId` | `String!` | Yes | The manifest's external ID |

**Returns**: `OperationResponse` (includes `count`, the number of executions marked for cancellation)

## triggerGroup

Triggers immediate execution of all enabled manifests in a group. A manifest that already has a queued work queue entry is not queued again and does not stop the others being queued; its entry is brought forward to now if it was due later. Every entry the trigger touches runs even if its manifest is disabled afterwards.

```graphql
mutation {
  operations {
    triggerGroup(groupId: 1) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

It goes through `IOperationsService.TriggerManifestGroupsAsync`, the call the dashboard's **Run Group** makes, with the one group.

**Returns**: `OperationResponse`. `count` is the number of manifests a new run was queued for; the message also counts the members that already had a queued run, which now runs as the trigger, for example `2 queued, 1 already queued (that entry now runs as the trigger) across 1 of 1 manifest group(s).` An unknown group id is counted as not found, with `count` 0.

## cancelGroup

Requests cancellation of every pending and running execution across all manifests in a group, by the same rule as [`cancelManifest`](#cancelmanifest).

```graphql
mutation {
  operations {
    cancelGroup(groupId: 1) {
      success
      count
      message
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | The manifest group's database ID |

**Returns**: `OperationResponse` (includes `count`, the number of executions marked for cancellation)

## triggerManifests

Triggers many manifests at once by database id, each as [`triggerManifest`](#triggermanifest) triggers one: an immediate run is queued, or a manifest's queued entry is brought forward to now when it already has one. It goes through `IOperationsService.TriggerManifestsAsync`, the call the dashboard's **Trigger Selected** (and **Trigger Selected (Ask Afresh)**) makes, so both count and note the same things.

```graphql
mutation {
  operations {
    triggerManifests(ids: [12, 13, 99999], askAfresh: false) {
      success
      matched
      queued
      alreadyQueued
      tooLateToAskAfresh
      skipped
      message
      notes { id message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids (not external ids). A repeated id counts once |
| `askAfresh` | `Boolean!` | No (default `false`) | `true` makes a queued retry the trigger releases ask its deciders again instead of replaying the failed run's decisions. One the dispatcher had already claimed still replays, and is counted in `tooLateToAskAfresh` |

**Returns**: `BatchTriggerResponse`. An unknown id is skipped with a note and does not stop the rest. An empty list, or more than 1000 ids, returns `success: false` with every count `0` and triggers nothing. Each manifest is queued with its own save, so the cost grows with the batch: about 2 s for a full batch of 1000 new entries at the stress suite's scale, and 0.6 s when each already had one. A save that fails on the database is a GraphQL error rather than a `success: false`; the manifests queued before it stay queued, and sending the same batch again is safe, since a manifest holds at most one queued entry.

### BatchTriggerResponse fields

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | `false` only when the batch was refused as given; then nothing was triggered |
| `matched` | `Int!` | How many of the ids named a manifest (for `triggerGroups`, a group) that exists and was triggered |
| `queued` | `Int!` | Manifests a new work queue entry was queued for |
| `alreadyQueued` | `Int!` | Manifests that already had a queued entry, so nothing more was queued: that entry became the triggered run, brought forward to now when it was due later. Also counts an entry the dispatcher claimed first, which is already running |
| `tooLateToAskAfresh` | `Int!` | Manifests triggered with `askAfresh: true` whose queued retry the dispatcher claimed first, so that run replays the failed run's decisions anyway. Each has a note naming the run it replays |
| `skipped` | `Int!` | Ids that named no manifest (or group). Each has a note |
| `message` | `String!` | One line for an operator |
| `notes` | `[BatchTriggerNote!]!` | One `{ id, message }` per id that did not get what the trigger asked for. Empty when every id was triggered as asked |

## triggerGroups

Triggers many manifest groups at once by id, each as [`triggerGroup`](#triggergroup) triggers one: every enabled member that runs on its own schedule is triggered, and `DEPENDENT` and `DORMANT_DEPENDENT` members are left to run after their parent. A disabled group is triggered too, as the single trigger triggers it, and holds its runs until it is enabled. It goes through `IOperationsService.TriggerManifestGroupsAsync`, the call the dashboard's groups page makes for **Trigger Selected**.

```graphql
mutation {
  operations {
    triggerGroups(ids: [1, 2]) { success matched queued alreadyQueued skipped message notes { id message } }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |

**Returns**: [`BatchTriggerResponse`](#batchtriggerresponse-fields). `matched` and `skipped` count group ids; `queued`, `alreadyQueued` and `tooLateToAskAfresh` count manifests. An empty list, or more than 1000 ids, returns `success: false` and triggers nothing. Its cost grows with the manifests the groups hold, one save each.

## cancelGroups

Requests cancellation of every pending and running execution of every manifest in the listed groups, by the rule [`cancelGroup`](#cancelgroup) applies to one, flagged in one update. It goes through `IOperationsService.CancelManifestGroupsAsync`, the call the dashboard's groups page makes for **Cancel Running**.

```graphql
mutation {
  operations {
    cancelGroups(ids: [1, 2]) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |

**Returns**: `OperationResponse`. `count` is the number of executions flagged, zero included, and `message` also says how many of the ids named a group. An empty list, or more than 1000 ids, returns `success: false` and flags nothing.

## setManifestsEnabled

Enables or disables many manifests at once. Only manifests whose flag differs are written. Backs the dashboard's bulk enable and disable.

```graphql
mutation {
  operations {
    setManifestsEnabled(ids: [3, 4, 5], enabled: false) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids |
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of manifests changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing.

## setManifestsReplayDecisionsOnRetry

Sets whether retries of many manifests replay the decisions of the run they retry. Only manifests
whose flag differs are written. It calls `IOperationsService.SetManifestsReplayDecisionsOnRetryAsync`,
the call the dashboard makes. Turning it off also clears the replay link of each manifest's queued
entry, in the same transaction as the flag, so a retry waiting out its backoff asks afresh, and the
message counts them (`"2 queued retry(s) no longer replay a failed run's decisions."`). See
[Retries replay decisions](/docs/scheduler/how-retries-work#retries-replay-decisions).

```graphql
mutation {
  operations {
    setManifestsReplayDecisionsOnRetry(ids: [3, 4], replay: false) { success count message }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest database ids |
| `replay` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of manifests changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing.

## updateManifest

Patches mutable settings on a single manifest. `input` is an `UpdateManifestInput`; each of its fields is independent; a `null`
value leaves it unchanged. Set `clearTimeout: true` to remove the per-execution timeout.

It goes through `IOperationsService.UpdateManifestAsync`, which runs every check before writing a
field. A value the scheduler could not use is refused with `success: false`, a message naming the
field, and nothing saved:

- `timeoutSeconds` of 0 or less (a zero timeout would cancel every run at once; use `clearTimeout`),
- `intervalSeconds` of 0 or less, `maxRetries` below 0, and `priority` outside 0 to 31 (the work
  queue's range),
- `scheduleType: CRON` with no `cronExpression` on the input or the manifest, or a `cronExpression`
  the scheduler cannot use: not 5 or 6 space-separated fields, a field out of range
  (`99 * * * *`), or an expression that never fires (`0 0 30 2 *`),
- `scheduleType: INTERVAL` with no `intervalSeconds` on the input or the manifest,
- a switch to `ONCE`, `DEPENDENT` or `DORMANT_DEPENDENT`, which need a time or a parent manifest this
  input cannot give. Schedule those from code.

The schedule is checked only when the input changes it (`scheduleType`, `cronExpression` or
`intervalSeconds`), so a manifest whose stored schedule is unusable can still be disabled.

```graphql
mutation {
  operations {
    updateManifest(id: 42, input: {
      isEnabled: false
      maxRetries: 5
      priority: 10
      scheduleType: CRON
      cronExpression: "0 3 * * *"
    }) { success message }
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `isEnabled` | `Boolean` | Enable/disable the manifest |
| `maxRetries` | `Int` | Retry budget |
| `priority` | `Int` | Dispatch priority, 0 to 31 |
| `timeoutSeconds` / `clearTimeout` | `Int` / `Boolean` | Per-execution timeout; `clearTimeout: true` removes it |
| `scheduleType` | `ScheduleType` | Schedule type |
| `cronExpression` | `String` | Cron expression |
| `intervalSeconds` | `Int` | Interval |

**Returns**: `OperationResponse` (`success: false` when the manifest id does not exist or a value is refused; on success `id` is the manifest's).

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests)
- [ITraxScheduler](/docs/sdk-reference/scheduler-api/manifest-management)
