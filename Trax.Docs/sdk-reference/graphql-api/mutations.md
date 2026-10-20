---
title: Mutations
description: "Reference for the Trax GraphQL mutation type: the dispatch and operations namespaces, every operations field and the page that documents it."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: GraphQL API
grand_parent: SDK Reference
has_children: true
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/RootMutation.cs
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/OperationsMutations.cs
---

# Mutations

The root mutation type, `RootMutation`, has two namespaces:

```graphql
type Mutation {
  dispatch: DispatchMutations!     # only when [TraxMutation] trains exist
  operations: OperationsMutations! # only when ExposeOperationMutations() is set
}
```

The root `Mutation` type is omitted when nothing contributes to it: no `[TraxMutation]` train and
no `ExposeOperationMutations()` call.

| Namespace | Resolved by | Holds | Page |
|---|---|---|---|
| `dispatch` | `DispatchMutations` | One generated field per train marked [`[TraxMutation]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch) |
| `operations` | `OperationsMutations` | Manifest, group, execution, machine instance and effect operations, and the nested namespaces below. Off by default: the host opts in with [`ExposeOperationMutations()`](/docs/sdk-reference/graphql-api/add-trax-graphql) | this page, below |
| `operations.config` | `ConfigMutations` | Scheduler settings and log levels | [operations.config mutations](/docs/sdk-reference/graphql-api/mutations/config) |
| `operations.manifestGroups` | `ManifestGroupMutations` | Group settings and enablement | [operations.manifestGroups mutations](/docs/sdk-reference/graphql-api/mutations/manifest-groups) |
| `operations.workQueue` | `WorkQueueMutations` | Queue, run and cancel work queue entries | [operations.workQueue mutations](/docs/sdk-reference/graphql-api/mutations/work-queue) |
| `operations.deadLetters` | `DeadLetterMutations` | Requeue and acknowledge dead letters | [operations.deadLetters mutations](/docs/sdk-reference/graphql-api/mutations/dead-letters) |

Most operations fields return [`OperationResponse`](/docs/sdk-reference/graphql-api/mutations/operation-response).

## Operations mutations

The whole `operations` namespace sits behind the operations gate (`GateOperations`,
`RequireAuthorization` or `AllowAnonymousOperations`; see
[AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)). The scheduler is reachable
through it, so an open namespace lets any caller change scheduled work. Mutations that enqueue a
train and input the caller chose (`requeueExecution`, `resumeExecution`, `workQueue.queueTrain`)
also apply that train's `[TraxAuthorize]` requirements. Mutations that enqueue what a manifest fixed
(`triggerManifest`, `triggerManifestDelayed`, `triggerGroup`, dead-letter requeues) answer to the
gate alone. See [Authorization: The Operations Surface](/docs/security/authorization-rules#the-operations-surface).

The fields directly under `operations`, each on the page that documents it:

### triggerManifest

Queue an immediate run of a manifest. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#triggermanifest)

### triggerManifestDelayed

Queue a run of a manifest after a delay. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#triggermanifestdelayed)

### disableManifest

[operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#disablemanifest)

### enableManifest

[operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#enablemanifest)

### cancelManifest

Cancel every pending and running execution of a manifest. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#cancelmanifest)

### triggerGroup

Trigger every enabled manifest of a group. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#triggergroup)

### cancelGroup

Cancel every pending and running execution of a group. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#cancelgroup)

### triggerManifests

Trigger many manifests by id. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#triggermanifests)

### triggerGroups

Trigger many groups by id. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#triggergroups)

### cancelGroups

Cancel the executions of many groups. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#cancelgroups)

### setManifestsEnabled

Enable or disable many manifests. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#setmanifestsenabled)

### setManifestsReplayDecisionsOnRetry

Set whether retries of many manifests replay decisions. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#setmanifestsreplaydecisionsonretry)

### updateManifest

Patch one manifest's schedule settings. [operations: manifest mutations](/docs/sdk-reference/graphql-api/mutations/manifests#updatemanifest)

### cancelExecution

Cancel one execution. [operations: execution mutations](/docs/sdk-reference/graphql-api/mutations/executions#cancelexecution)

### cancelExecutions

Cancel many executions. [operations: execution mutations](/docs/sdk-reference/graphql-api/mutations/executions#cancelexecutions)

### requeueExecution

Queue a new run of an execution's train with its recorded input. [operations: execution mutations](/docs/sdk-reference/graphql-api/mutations/executions#requeueexecution)

### resumeExecution

Queue a run that resumes a failed or cancelled execution from a checkpoint. [operations: execution mutations](/docs/sdk-reference/graphql-api/mutations/executions#resumeexecution)

### cancelMachineInstance

Cancel a system-owned state machine instance's live run. [operations: machine instance mutations](/docs/sdk-reference/graphql-api/mutations/machine-instances#cancelmachineinstance)

### startBulkOperation

Cancel, requeue, acknowledge, trigger, enable or disable every row a list filter matches, in the background, tracked. [operations: bulk operations](/docs/sdk-reference/graphql-api/mutations/bulk-operations#startbulkoperation)

### setEffectEnabled

Turn an effect on or off in the API process. [operations: effect mutations](/docs/sdk-reference/graphql-api/mutations/effects#seteffectenabled)

### configureEffect

Change an effect's runtime settings in the API process. [operations: effect mutations](/docs/sdk-reference/graphql-api/mutations/effects#configureeffect)

## config (nested namespace)

`updateScheduler` and `setLogLevels`: [operations.config mutations](/docs/sdk-reference/graphql-api/mutations/config).

## manifestGroups (nested namespace)

`updateManifestGroup`, `setManifestGroupsEnabled` and `setAllManifestGroupsEnabled`: [operations.manifestGroups mutations](/docs/sdk-reference/graphql-api/mutations/manifest-groups).

## workQueue (nested namespace)

`queueTrain`, `runTrain`, `cancelWorkQueueEntry` and `cancelWorkQueueEntries`: [operations.workQueue mutations](/docs/sdk-reference/graphql-api/mutations/work-queue).

### queueTrain

[operations.workQueue mutations](/docs/sdk-reference/graphql-api/mutations/work-queue#queuetrain)

### runTrain

[operations.workQueue mutations](/docs/sdk-reference/graphql-api/mutations/work-queue#runtrain)

## deadLetters (nested namespace)

`requeueDeadLetter`, `acknowledgeDeadLetter`, `requeueDeadLetters`, `acknowledgeDeadLetters`,
`requeueAllDeadLetters` and `acknowledgeAllDeadLetters`: [operations.deadLetters mutations](/docs/sdk-reference/graphql-api/mutations/dead-letters).

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [OperationResponse](/docs/sdk-reference/graphql-api/mutations/operation-response)
- [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
- [Error codes](/docs/sdk-reference/graphql-api/error-codes)
