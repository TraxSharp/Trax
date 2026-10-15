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

Rolls up the processes that have executed trains, grouped by `HostInstanceId` from the metadata table. Backs the dashboard's cluster view across a split API + scheduler + worker deployment. Rows without a stamped host instance are ignored.

```graphql
query {
  operations {
    hosts {
      instanceId
      name
      environment
      lastSeen
      totalExecutions
      currentlyRunning
    }
  }
}
```

This is a full aggregation over the metadata table (like `metrics.dashboard`), so treat it as an occasional-refresh read, not a hot poll. `lastSeen` is the most recent execution start on the host, the freshness signal the dashboard renders; there is no separate heartbeat.

**Returns**: `[HostInfo!]!`

### HostInfo fields

| Field | Type | Description |
|-------|------|-------------|
| `instanceId` | `String!` | Stable per-process id (lives for the process's lifetime) |
| `name` | `String` | Machine / container host name, if stamped |
| `environment` | `String` | Hosting environment (e.g. `Production`), if stamped |
| `lastSeen` | `DateTime!` | Most recent execution start on this host |
| `totalExecutions` | `Long!` | Total executions attributed to this host |
| `currentlyRunning` | `Int!` | Executions on this host still `InProgress` |

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

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions)
- [operations: effect mutations](/docs/sdk-reference/graphql-api/mutations/effects)
