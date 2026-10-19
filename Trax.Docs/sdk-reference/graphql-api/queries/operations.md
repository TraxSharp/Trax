---
title: "operations: system queries"
description: "Reference for the operations queries about the host: health, registered trains, admin train names, hosts, per-train stats and effects."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.cs
---

# operations: system queries

Fields of `OperationsQueries`, the type behind `query { operations { ... } }`, that describe the host and its registered trains. The namespace exists only when the host calls [`ExposeOperationQueries()`](/docs/sdk-reference/graphql-api/add-trax-graphql), and every field answers to the operations gate.

## health

Returns the current health status of the Trax scheduler system. This is the same data reported by the ASP.NET `IHealthCheck` at `/trax/health`, exposed as a structured GraphQL type.

It is the dashboard's home page read, behind the operations gate like every field here. A monitor, load balancer or scraper reads `/trax/health` instead ([AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) and `MapHealthChecks`), which needs no GraphQL document and no operator credential.

```graphql
query {
  operations {
    health {
      status
      description
      queueDepth
      inProgress
      failedLastHour
      deadLetters
    }
  }
}
```

**Returns**: `HealthStatus!`

### HealthStatus fields

| Field | Type | Description |
|-------|------|-------------|
| `status` | `String!` | `"Healthy"` or `"Degraded"` |
| `description` | `String!` | Human-readable summary |
| `queueDepth` | `Int!` | Work items with status `Queued` |
| `inProgress` | `Int!` | Executions with `TrainState.InProgress` |
| `failedLastHour` | `Int!` | Failed executions in the last hour |
| `deadLetters` | `Int!` | Dead letters with status `AwaitingIntervention` |

Status is `Degraded` when `deadLetters > 0` or `failedLastHour > 10`.

## trains

Returns every train registered in the DI container, including a runtime-generated input schema describing each property on the input type. Pass `hideAdminTrains: true` to exclude the framework's internal scheduler trains (manifest manager, job dispatcher, dead letter cleanup, etc.) from the result; the dashboard uses this flag when its "Hide admin trains" toggle is on.

```graphql
query {
  operations {
    trains {
      fullName
      serviceTypeName
      implementationTypeName
      inputTypeName
      outputTypeName
      lifetime
      inputSchema {
        name
        typeName
        isNullable
        enumValues
      }
    }
  }
}
```

**Returns**: `[TrainInfo!]!`

### TrainInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `fullName` | `String!` | The train's canonical name, its service interface's FullName (e.g. `MyApp.Trains.IProcessOrderTrain`). This is the name every field that takes a train accepts: [`queueTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#queuetrain) and [`runTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#runtrain), [`trainStats`](#trainstats), and the `trainName` filters on [`executions`](/docs/sdk-reference/graphql-api/queries/executions#executions) and `workQueues` |
| `serviceTypeName` | `String!` | Friendly name of the service interface (e.g. `IServiceTrain<OrderInput, OrderResult>`), for display. No field that takes a train accepts it; use `fullName` |
| `implementationTypeName` | `String!` | Friendly name of the concrete class |
| `inputTypeName` | `String!` | Friendly name of the input type |
| `outputTypeName` | `String!` | Friendly name of the output type |
| `lifetime` | `String!` | DI lifetime (`Singleton`, `Scoped`, `Transient`) |
| `inputSchema` | `[InputPropertySchema!]!` | Public readable properties on the input type |
| `requiredPolicies` | `[String!]!` | The policies the train's `[TraxAuthorize]` attributes name |
| `requiredRoles` | `[String!]!` | The roles the train's `[TraxAuthorize]` attributes name |
| `isQuery` | `Boolean!` | Whether the train carries [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) |
| `isMutation` | `Boolean!` | Whether the train carries `[TraxMutation]` |
| `graphQLName` | `String` | The `Name` set on `[TraxQuery]` or `[TraxMutation]`; null when the field name is derived |
| `isBroadcastEnabled` | `Boolean!` | Whether the train carries [`[TraxBroadcast]`](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) |
| `hasQueueSubjectKey` | `Boolean!` | Whether the train overrides [`QueueSubjectKey`](/docs/mediator/queued-work#two-mutations-for-one-record), so its queued runs for one subject run one at a time (an override may still return no key for a given input). A run started with [`runTrain`](/docs/sdk-reference/graphql-api/mutations/work-queue#runtrain) bypasses that serialization and may run alongside queued or in-flight work for the same subject; the dashboard's Run dialog warns about it, and a client offering Run should too |
| `chain` | `RunGraph!` | The train's declared chain, read without a run: the same nodes, in the same order and places, that [`runGraph`](/docs/sdk-reference/graphql-api/queries/executions#rungraph) draws a run of it on, every one `NOT_REACHED`, with no steps and `metadataId` 0. `hasGraph` is false when the host cannot read the train's chain. Read only when selected, from the chain the host reads once per train |
| `asksDecisions` | `Boolean!` | True when the train declares a step that asks a decider and records the decision: a `Decide`, or a `Switch`, `Gate` or `Scale` that asks its own question. Only such a train's runs have decisions a requeue can [replay or ask afresh](/docs/sdk-reference/graphql-api/mutations/executions). False when the host cannot read the train's chain |

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `hideAdminTrains` | `Boolean` | `false` | When `true`, filters out framework-internal scheduler trains (matches `AdminTrains.FullNames` in `Trax.Scheduler.Configuration`) |

### InputPropertySchema fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | The JSON name the input reader expects: the property name under the system JSON naming policy (camelCase by default), or its `[JsonPropertyName]` when it has one. A property marked `[JsonIgnore]` is left out |
| `typeName` | `String!` | Friendly type name (e.g. `String`, `Int32`, `DateTime?`) |
| `isNullable` | `Boolean!` | Whether the property is nullable |
| `enumValues` | `[String!]` | The accepted values when the property is an enum or a nullable enum, spelled as the reader expects them. Null for any other type |

The names and enum spellings come from the same options `queueTrain` and `runTrain` deserialize
input with, so a client that builds its JSON from this schema writes what the reader accepts.

## adminTrainNames

Returns the canonical FullNames of the framework's internal scheduler trains (job dispatcher, manifest manager, job runner, cleanup). This is the same list `hideAdminTrains` filters against on the `trains`, `manifests` and `executions` queries and the `metrics.dashboard` query.

```graphql
query {
  operations {
    adminTrainNames
  }
}
```

The subscription feed streams every train on an admin host, so the dashboard reads this list once and filters the live `onTrainStateChanged` events client-side by the same rule the server applies to the grid, keeping the feed and grid consistent when "Hide admin trains" is on.

**Returns**: `[String!]!`

## hosts

The host processes that have beaten in the last day, from `trax.host_heartbeat`. Every host with a data provider
rewrites its row every 15 seconds whether or not it has work ([Host Tracking](/docs/sdk-reference/configuration/host-tracking#heartbeat)),
so the list shows an idle host as online and a dead one as offline. Online hosts come first, then by name.

```graphql
query {
  operations {
    hosts {
      instanceId
      name
      environment
      traxVersion
      startedAt
      lastHeartbeat
      online
      runsDispatcher
      runsManifestManager
      workerSlots
      workerSlotsInUse
      currentlyRunning
    }
  }
}
```

It reads the small heartbeat table and counts each listed host's runs in progress, so it is cheap at any history
size. A host on a version from before the heartbeat writes no row and is not listed.

**Returns**: `[HostInfo!]!`

### HostInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `instanceId` | `String!` | The process's instance id, the one stamped on each run it executes |
| `name` | `String!` | Machine or container host name |
| `environment` | `String!` | Hosting environment, such as `server`, `ecs`, `kubernetes` or `lambda` |
| `traxVersion` | `String!` | The Trax version the process runs |
| `startedAt` | `DateTime!` | When the process began beating |
| `lastHeartbeat` | `DateTime!` | When the process last wrote its row |
| `online` | `Boolean!` | `true` while the last heartbeat is under 45 seconds old (three beats) |
| `runsDispatcher` | `Boolean!` | The process runs the job dispatcher, switched on |
| `runsManifestManager` | `Boolean!` | The process runs the manifest manager, switched on |
| `workerSlots` | `Int` | Local workers the process runs, `null` with none |
| `workerSlotsInUse` | `Int` | Workers running a job at the last heartbeat, `null` with none |
| `currentlyRunning` | `Int!` | Runs attributed to this host still `InProgress` |

## trainStats

Execution roll-up for a single train, keyed by its interface FullName (the value stored in `metadata.Name`). Backs the summary cards on the dashboard's per-train detail page. The state grouping and `ix_metadata_*` indexes keep it cheap against a large metadata table.

```graphql
query {
  operations {
    trainStats(trainName: "MyApp.Trains.Reports.IBuildDailyReportTrain") {
      total
      completed
      failed
      inProgress
      averageMilliseconds
      lastRun
      lastSuccessfulRun
    }
  }
}
```

**Returns**: `TrainExecutionStats!`

### TrainExecutionStats fields

| Field | Type | Description |
|-------|------|-------------|
| `trainName` | `String!` | The interface FullName the stats are scoped to |
| `total` | `Long!` | Total executions of this train |
| `completed` / `failed` / `inProgress` / `pending` / `cancelled` | `Long!` | Counts by state |
| `lastRun` | `DateTime` | Start time of the most recent execution. Null when there are none |
| `lastSuccessfulRun` | `DateTime` | End time of the most recent `Completed` execution. Null when there are none |
| `averageMilliseconds` | `Float` | Mean duration of completed executions. Null when there are none |

## effects

Lists the observational effects registered in the API process, with their enabled and toggleable state and, for an effect whose factory exposes runtime settings, those settings: as JSON, and as `fields` a settings editor can be built from. It reads through `IEffectSettingsService` in Trax.Scheduler, the service the dashboard's effects page calls, so both show the same values. Empty when the host registers no effect registry.

The effect registry and each settings object are in-memory, per-process singletons with no persistence or cross-process broadcast, so this reflects the API host only, not the scheduler/worker processes where effects actually run. [`setEffectEnabled`](/docs/sdk-reference/graphql-api/mutations/effects#seteffectenabled) toggles an effect and [`configureEffect`](/docs/sdk-reference/graphql-api/mutations/effects#configureeffect) changes its settings in this same process. Changing effect state across a distributed deployment would need a shared store plus a change broadcast, which is not built.

```graphql
query {
  operations {
    effects {
      name
      fullName
      enabled
      toggleable
      isConfigurable
      configurationTypeName
      configuration
      fields {
        name
        typeName
        kind
        nullable
        enumValues
        sensitive
        hasValue
        value
        hint
      }
    }
  }
}
```

**Returns**: `[EffectInfo!]!`, ordered by `fullName`.

### EffectInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | Effect factory type name (short) |
| `fullName` | `String!` | Effect factory type FullName |
| `enabled` | `Boolean!` | Whether the effect is currently enabled in this process |
| `toggleable` | `Boolean!` | Whether the effect can be toggled (infrastructure effects are always on) |
| `isConfigurable` | `Boolean!` | Whether the effect's factory exposes runtime settings (implements `IConfigurableProviderFactory`) |
| `configurationTypeName` | `String` | FullName of the settings type. Null when not configurable |
| `configuration` | `String` | The factory's current settings as camelCase JSON, with each `[TraxSensitive]` member written as `{"_redacted": true}`. Null when not configurable, or when the settings type cannot be written as JSON |
| `fields` | `[EffectSettingInfo!]!` | One per public read-write property of the settings type, editable ones first in declaration order. Empty when not configurable |

### EffectSettingInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | `String!` | The property's name, which `configureEffect` takes |
| `typeName` | `String!` | The property's type, without `Nullable<>`, such as `Int32` or `TimeSpan` |
| `kind` | `EffectFieldKind!` | How it is edited: `BOOLEAN` (a switch), `ENUM` (one of `enumValues`), `TEXT` (text read as the type: a number, string, date, time, duration, GUID or character) or `SET_IN_CODE` (a delegate, collection or object, which cannot be written here) |
| `nullable` | `Boolean!` | Whether it accepts no value; a null or blank value writes null |
| `enumValues` | `[String!]` | The member names, for an enum. Null otherwise |
| `sensitive` | `Boolean!` | `true` for a property marked `[TraxSensitive]`: `value` is always null. It can still be written |
| `hasValue` | `Boolean!` | Whether the setting holds a value. `true` for a sensitive setting that holds one, so a client can show it is set without showing it |
| `value` | `String` | The current value as text, in the form `configureEffect` reads back. Null when there is none, when the setting is sensitive, and for `SET_IN_CODE` |
| `hint` | `String!` | What to type, such as `yyyy-MM-dd HH:mm:ss (UTC)` |

Settings can hold credentials. Like an execution's `input`, they are reachable only under the
`operations` namespace, so the gate you put on it (`GateOperations` or `RequireAuthorization`)
decides who reads them. See [Train inputs and the operations gate](/docs/sdk-reference/graphql-api/queries#train-inputs-and-the-operations-gate).

## lookup

Finds what an id, key or name names when you do not know its kind, the read behind the console's search. It calls
[`IOperationsService.LookupAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#lookupasync).

```graphql
query {
  operations {
    lookup(text: "7f3c0e2a9b1d4c5e8f6a7b8c9d0e1f2a") {
      kind
      id
      label
      matchedOn
      machine
      ownerKind
      rowId
    }
  }
}
```

**Returns**: `[LookupMatch!]!`, empty when nothing matches.

| Field | Type | Description |
|-------|------|-------------|
| `kind` | `LookupKind!` | `RUN`, `MANIFEST`, `WORK_QUEUE_ENTRY`, `DEAD_LETTER`, `MACHINE_INSTANCE` or `TRAIN` |
| `id` | `String!` | The id the kind's read takes: a run, manifest, entry or dead letter id, an instance's GUID, or a train's `fullName` |
| `label` | `String!` | The train, manifest or machine, with its state |
| `matchedOn` | `String!` | The field the text matched: `id`, `externalId`, `name` or `subjectKey` |
| `machine` | `String` | For an instance, its machine; null otherwise |
| `ownerKind` | `SnapshotOwnerKind` | For an instance, who owns it; null otherwise |
| `rowId` | `Long` | For an instance, the row [`machineInstance`](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstance) takes for a user's draft; null otherwise |

Matches are exact, after trimming: runs by id and external id, manifests by id, external id and name, work queue
entries by id, external id and subject key (those still queued or dispatched), dead letters by id, state-machine
instances by id, and registered trains by full or friendly name. Each is one read of an indexed column, at most 10
matches of each kind. An instance is named the way [`machineInstances`](/docs/sdk-reference/graphql-api/queries/machine-instances)
names it, never by its context or whose a user's draft is. Like every field here it answers to the operations gate.

## attention

What needs an operator's attention, the read behind the console's Overview. It calls
[`IOperationsService.GetAttentionAsync`](/docs/sdk-reference/scheduler-api/i-operations-service#getattentionasync).

```graphql
query {
  operations {
    attention {
      asOf
      scheduler { manifestManagerEnabled jobDispatcherEnabled paused link }
      deadLetters { trainName reason count oldestDeadLetteredAt link }
      failingTrains { trainName failedLastHour finishedLastHour lastHourFailureRate baselineFailureRate link }
      stuckInstances { machine state count link }
      queue { queued oldestQueuedAt oldestAgeSeconds link }
      offlineHosts { instanceId hostName lastHeartbeatAt runsDispatcher link }
    }
  }
}
```

**Returns**: `Attention!`.

| Field | Type | Description |
|-------|------|-------------|
| `scheduler` | `AttentionScheduler!` | Whether the manifest manager and the job dispatcher are switched on; `paused` is true when either is off |
| `deadLetters` | `[AttentionDeadLetterGroup!]!` | Dead letters awaiting intervention, one group per train and reason, with `count` and `oldestDeadLetteredAt`, largest first |
| `failingTrains` | `[AttentionFailingTrain!]!` | Trains whose failure rate among runs started in the last hour is above their rate over the seven days before it, highest first |
| `stuckInstances` | `[AttentionStuckInstances!]!` | Stuck state-machine instances, one group per machine and state, largest first |
| `queue` | `AttentionQueue!` | Queued entries that are confirmed and due, and how long the oldest has waited |
| `offlineHosts` | `[AttentionHost!]!` | Hosts that have missed three heartbeats, most recently seen first |
| `asOf` | `DateTime!` | When the read was made |

Each list holds at most 50 entries. Every entry has a `link`: the console path and query that lists what it counts, such
as `/runs?state=FAILED&train=Shop.ICheckoutTrain` or `/machines/order?state=Waiting&stuck=true`. A stuck-instance group
names a machine and a state, never an instance's owner or context. Like every field here it answers to the operations
gate.

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions)
- [operations: effect mutations](/docs/sdk-reference/graphql-api/mutations/effects)
