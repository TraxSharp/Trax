---
title: "operations.config mutations"
description: "Reference for the operations.config mutation namespace: updateScheduler, setLogLevels, and how saved scheduler settings are stored and applied."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 6
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/ConfigMutations.cs
---

# operations.config mutations

`ConfigMutations`, the type behind `mutation { operations { config { ... } } }`.

The `operations.config` namespace patches scheduler runtime settings. A save writes only the fields it sets to the persisted `trax.scheduler_config` row, so it never rewrites a setting it did not name, and applies them to the host that received it at once. Every running scheduler host reads the row every few seconds and applies a new or changed one without a restart, so a save made on an API-only host, or on one of several scheduler hosts, reaches all of them. A scheduler applies a change from its next polling cycle, including a new polling or cleanup interval; `localWorkerCount` is the exception and applies when the worker pool next starts. The row also survives restarts: each scheduler applies it at startup over the settings configured in code.

The row records which settings a save named, in its `overrides`. A setting no save has named is not stored, so each scheduler keeps the value configured in code for it, and a later change in code applies to it. That is why any host can make a save, the first one included, whether or not it runs the scheduler: an API-only host built with `AddTraxJobRunner()` never has to supply values for settings it did not name. Two hosts making the first save at the same time both succeed; the one that loses the race applies its patch to the row the other created.

Which fields a save stores depends on the host. A field whose setting the row already names is stored when it differs from the stored value. For a setting the row does not name, a scheduler host stores the field only when it differs from the value that host runs with, so a save from the dashboard on a scheduler host leaves unchanged fields out. A host that does not run the scheduler cannot know that value, so it stores every field the patch sets, and each field it sends becomes a saved value that replaces the code value on every scheduler. `count` counts the fields stored.

A stored value takes precedence over the value in code until it is changed or the row is deleted, and deleting the row returns every running scheduler to its configured settings. Each scheduler logs a warning when a saved value replaces a different value configured in code, because a deploy that changes that setting in code then has no effect. The two dead-letter purge settings are the exception to "the saved value wins": when code states them too, the purge runs only if both `autoPurgeDeadLetters` values allow it, and the longer of the two `deadLetterRetentionPeriod` values applies (see [Dead Letter Auto-Purge](/docs/scheduler/dead-letters-and-cleanup#dead-letter-auto-purge)).

A scheduler that cannot read the row when it starts (the database is briefly unreachable, or the table is not migrated yet) logs a warning, runs with its code values, and applies the row at its first successful read.

## updateScheduler

Patches one or more fields. Fields left out of `input` are unchanged. The persisted row's `updatedAt` only moves on real changes (no-op patches are ignored at the DB layer).

```graphql
mutation {
  operations {
    config {
      updateScheduler(input: {
        defaultMaxRetries: 5
        defaultJobTimeout: "00:30:00"
      }) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `input` | `UpdateSchedulerConfigInput!` | Yes | Patch payload (fields below) |

## setLogLevels

Sets the level each listed log category filters at in the API process, through `ILogLevelService.SetLogLevels` in Trax.Scheduler, the call the dashboard's server settings make. [`config.logLevels`](/docs/sdk-reference/graphql-api/queries/config#environmentname-version-and-loglevels) reads the result.

```graphql
mutation {
  operations {
    config {
      setLogLevels(levels: [
        { category: "Default", level: WARNING }
        { category: "Trax", level: DEBUG }
      ]) { success count notApplied message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `levels` | `[LogLevelSettingInput!]!` | Yes | 1 to 1000 `{ category: String!, level: LogLevel! }`. `category` is a category configured under `Logging:LogLevel` (or changed here before), case ignored; `level` is `TRACE`, `DEBUG`, `INFORMATION`, `WARNING`, `ERROR`, `CRITICAL` or `NONE`. When a category is listed twice, the last level wins |

**Returns**: `SetLogLevelsResponse` with `success: Boolean!`, `count: Int!` (categories set), `notApplied: [String!]!` and `message: String!`. A list naming a category that is not configured, or an empty list, is refused whole and nothing changes. `notApplied` lists categories set whose loggers still filter at another level, which happens when the host sets the logger filter itself after Trax.

The change is applied to the host's logger filter options over every configuration source and lasts until the process restarts; nothing is persisted, and it does not reach the scheduler or worker processes. A host that does not call `AddScheduler` registers no `ILogLevelService`, and there `setLogLevels` returns `success: false`.

## UpdateSchedulerConfigInput fields

Every field defaults to `null` and means "no change". To clear `maxActiveJobs` (set to "no per-group cap") or `localWorkerCount` (reset to `Environment.ProcessorCount`), set the corresponding `clear*` flag to `true`.

| Field | Type | Description |
|-------|------|-------------|
| `manifestManagerEnabled` | `Boolean` | |
| `jobDispatcherEnabled` | `Boolean` | |
| `manifestManagerPollingInterval` | `TimeSpan` | 1 second to 30 days |
| `jobDispatcherPollingInterval` | `TimeSpan` | 1 second to 30 days |
| `maxActiveJobs` | `Int` | At least 1 |
| `clearMaxActiveJobs` | `Boolean` | When `true`, sets `maxActiveJobs` to null |
| `defaultMaxRetries` | `Int` | Zero or more |
| `failureCountWindow` | `TimeSpan` | 1 second to ten years. How far back failed runs count toward retry backoff and `MaxRetries` |
| `defaultRetryDelay` | `TimeSpan` | Zero to ten years |
| `retryBackoffMultiplier` | `Float` | At least 1 |
| `maxRetryDelay` | `TimeSpan` | Zero to ten years |
| `defaultJobTimeout` | `TimeSpan` | 1 second to ten years |
| `stalePendingTimeout` | `TimeSpan` | 1 second to ten years |
| `recoverStuckJobsOnStartup` | `Boolean` | |
| `deadLetterRetentionPeriod` | `TimeSpan` | Zero to ten years |
| `autoPurgeDeadLetters` | `Boolean` | |
| `localWorkerCount` | `Int` | 1 to 256. Ignored when this process runs no local worker pool (see [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers)). Applies when the worker pool next starts |
| `clearLocalWorkerCount` | `Boolean` | Resets `localWorkerCount` to `Environment.ProcessorCount` |
| `metadataCleanupInterval` | `TimeSpan` | 1 second to 30 days. Ignored when metadata cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | 1 second to ten years. Ignored when metadata cleanup is not configured |

**Returns**: `OperationResponse`. `count` is the number of fields actually changed (zero if every supplied value already matched). A value outside its range makes `success` `false`, with a `message` naming each offending field, and nothing in the patch is applied or persisted; so does a first save on a host that does not run the scheduler. The ranges are what the scheduler can run with: an interval is the wait between two polling cycles, which a timer caps at about 49 days, and polling the database more often than once a second is load rather than responsiveness. When a scheduler applies the row, a persisted value outside its range (from a row written before these checks, or edited by hand) is skipped with a warning, and the configured value stays in effect.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations.config queries](/docs/sdk-reference/graphql-api/queries/config)
