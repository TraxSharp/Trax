---
title: Queries
description: "Reference for the Trax GraphQL query type: the discover and operations namespaces, every operations field and the page that documents it."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: GraphQL API
grand_parent: SDK Reference
has_children: true
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/RootQuery.cs
  - Trax.Api/src/Trax.Api.GraphQL/Queries/OperationsQueries.cs
---

# Queries

The root query type, `RootQuery`, has two namespaces:

```graphql
type Query {
  discover: DiscoverQueries!
  operations: OperationsQueries!  # only when ExposeOperationQueries() is set
}
```

| Namespace | Resolved by | Holds | Page |
|---|---|---|---|
| `discover` | `DiscoverQueries` | One generated field per train marked [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute), and the query model fields | [discover queries](/docs/sdk-reference/graphql-api/queries/discover) |
| `operations` | `OperationsQueries` | Health, trains, manifests, executions, machine instances, and the nested namespaces below. Off by default: the host opts in with [`ExposeOperationQueries()`](/docs/sdk-reference/graphql-api/add-trax-graphql) | this page, below |
| `operations.deadLetters` | `DeadLetterQueries` | Dead letters and the requeue-all job | [operations.deadLetters queries](/docs/sdk-reference/graphql-api/queries/dead-letters) |
| `operations.config` | `ConfigQueries` | Scheduler settings, environment, version, log levels | [operations.config queries](/docs/sdk-reference/graphql-api/queries/config) |
| `operations.metrics` | `MetricsQueries` | Dashboard metrics and process metrics | [operations.metrics queries](/docs/sdk-reference/graphql-api/queries/metrics) |
| `operations.logs` | `LogQueries` | The `trax.log` table | [operations.logs queries](/docs/sdk-reference/graphql-api/queries/logs) |
| `operations.manifestGroups` | `ManifestGroupQueries` | Manifest groups and their dependency graphs | [operations.manifestGroups queries](/docs/sdk-reference/graphql-api/queries/manifest-groups) |
| `operations.workQueue` | `WorkQueueQueries` | The work queue | [operations.workQueue queries](/docs/sdk-reference/graphql-api/queries/work-queue) |

Every paged field returns [`PagedResult`](/docs/sdk-reference/graphql-api/queries/paged-result).

## Operations queries

The fields directly under `operations`, each on the page that documents it.

### health

The scheduler's health, as the `/trax/health` check reports it. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#health)

### trains

Every registered train with its input schema. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#trains)

### adminTrainNames

The full names of the scheduler's own trains. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#admintrainnames)

### hosts

The processes that have run trains. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#hosts)

### trainStats

Run counts for one train. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#trainstats)

### effects

The effects registered in the API process and their settings. [operations: system queries](/docs/sdk-reference/graphql-api/queries/operations#effects)

### manifests

The paged manifest list. [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests#manifests)

### manifest

One manifest by id. [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests#manifest)

### manifestDetail

One manifest with its input and scheduling settings. [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests#manifestdetail)

### manifestStats

Run counts for one manifest. [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests#manifeststats)

### manifestExclusions

A manifest's exclusion windows. [operations: manifest queries](/docs/sdk-reference/graphql-api/queries/manifests#manifestexclusions)

### executions

The paged, filtered execution list. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#executions)

### execution

One execution by id. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#execution)

### executionDetail

One execution with its input, output and stack trace. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#executiondetail)

### executionChildren

The runs started from inside one run. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#executionchildren)

### junctionRuns

The steps one run recorded. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#junctionruns)

### declaredChain

A train's declared chain as a graph. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#declaredchain)

### runGraph

One run drawn on its train's declared chain. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#rungraph)

### decisions

One run's recorded decisions. [operations: execution queries](/docs/sdk-reference/graphql-api/queries/executions#decisions)

### machineInstances

The paged list of persisted state machine instances. [operations: machine instance queries](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstances)

### machineInstance

One instance with the runs it invoked. [operations: machine instance queries](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstance)

### machineInstanceCounts

Instance counts by machine, state and owner kind. [operations: machine instance queries](/docs/sdk-reference/graphql-api/queries/machine-instances#machineinstancecounts)

## deadLetters (nested under operations)

`deadLetters`, `deadLetter` and `requeueAllJob`: [operations.deadLetters queries](/docs/sdk-reference/graphql-api/queries/dead-letters).

## config (nested under operations)

`scheduler`, `environmentName`, `version` and `logLevels`: [operations.config queries](/docs/sdk-reference/graphql-api/queries/config).

## metrics (nested under operations)

`dashboard`, `server` and `serverCpuPercent`: [operations.metrics queries](/docs/sdk-reference/graphql-api/queries/metrics).

## logs (nested under operations)

`logs`: [operations.logs queries](/docs/sdk-reference/graphql-api/queries/logs).

## manifestGroups (nested under operations)

`groups`, `group`, `stats`, `graph` and `dependencyGraph`: [operations.manifestGroups queries](/docs/sdk-reference/graphql-api/queries/manifest-groups).

## workQueue (nested under operations)

`workQueues`, `workQueue` and `detail`: [operations.workQueue queries](/docs/sdk-reference/graphql-api/queries/work-queue).

### detail

One work queue entry with its input. [operations.workQueue queries](/docs/sdk-reference/graphql-api/queries/work-queue#detail)

## Train inputs and the operations gate

An execution's `input`, a manifest's `properties` and a work queue entry's `input` are on
[`executionDetail`](/docs/sdk-reference/graphql-api/queries/executions#executiondetail),
[`manifestDetail`](/docs/sdk-reference/graphql-api/queries/manifests#manifestdetail) and
[`workQueue.detail`](/docs/sdk-reference/graphql-api/queries/work-queue#detail) only, never on a
list type, and an effect's settings are on
[`effects`](/docs/sdk-reference/graphql-api/queries/operations#effects). All of them sit under the
`operations` namespace, so whatever gates it (`RequireAuthorization()` or `GateOperations(...)`, see
[AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)) decides who reads them. There is
no separate field-level gate: a caller who can read one execution's input can read a manifest's
properties too. [Authorization](/docs/authorization#the-operations-surface) explains the choice.

A member marked [`[TraxSensitive]`](/docs/sdk-reference/configuration/save-train-parameters#masking-sensitive-fields)
is masked in all three, written as `{"_redacted": true}`. An execution's `input` is recorded that
way. A manifest's `properties` and a work queue entry's `input` are stored with the real value,
because a run starts from them, so the read masks them: it reads the stored JSON back as the input
type of the train registered on this host and writes it with the mask. When the host has no
registered train whose input type the copy names, or the JSON does not read as that type, the whole
value reads as `{"_redacted": true}`.

A member whose declared type does not say what it holds is masked on those two reads whether or not
anything under it is marked: `object`, `JsonElement`, `JsonNode`, `JsonDocument`, a collection
without an element type, and a collection or dictionary whose elements are one of those. The copy
was stored from the runtime value, so `record Pay(object Details)` holding a `Card` with a
`[TraxSensitive] Number` stores the number, and reading it back as `Pay` gives JSON with no member
left to mark. Such a member reads as `{"_redacted": true}`; the members whose types are declared
read as stored.

An effect's settings on `effects` are written from the settings object itself, so a
`[TraxSensitive]` member at any depth there reads as `{"_redacted": true}` too.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions)
- [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
- [Error codes](/docs/sdk-reference/graphql-api/error-codes)
