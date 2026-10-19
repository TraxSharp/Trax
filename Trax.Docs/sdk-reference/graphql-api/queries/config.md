---
title: "operations.config queries"
description: "Reference for the operations.config query namespace: the live scheduler settings, the host's environment name, Trax version and log levels."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 7
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/ConfigQueries.cs
---

# operations.config queries

`ConfigQueries`, the type behind `operations { config { ... } }`.

The `operations.config` namespace returns the live scheduler runtime settings (the dashboard-editable subset of `SchedulerConfiguration`, `LocalWorkerOptions`, and `MetadataCleanupConfiguration`). The dashboard's Server Settings page and this query both read from the same in-memory singleton, so they agree. The page saves through the same operations call as `updateScheduler`, sending only the fields the operator changed, and reloads from this snapshot afterwards.

Persistence: settings written via `operations.config.updateScheduler` (or the dashboard) are stored in the singleton-row `trax.scheduler_config` table, only the ones a save names. Every running scheduler applies them to its in-memory singleton at startup and re-reads the row every few seconds, so they survive restarts and reach every scheduler host. A setting never saved keeps each host's configured value. See [config](/docs/sdk-reference/graphql-api/mutations/config) for which fields a save stores.

## scheduler

```graphql
query {
  operations {
    config {
      scheduler {
        manifestManagerEnabled
        jobDispatcherEnabled
        manifestManagerPollingInterval
        jobDispatcherPollingInterval
        maxActiveJobs
        defaultMaxRetries
        failureCountWindow
        defaultRetryDelay
        retryBackoffMultiplier
        maxRetryDelay
        defaultJobTimeout
        stalePendingTimeout
        recoverStuckJobsOnStartup
        deadLetterRetentionPeriod
        autoPurgeDeadLetters
        localWorkerCount
        metadataCleanupInterval
        metadataCleanupRetention
      }
    }
  }
}
```

**Returns**: `SchedulerConfigSnapshot`.

## environmentName, version and logLevels

The API host's environment, the Trax version it runs, and the level each configured log category
filters at, which the dashboard shows as its environment badge, its footer and on its server
settings page.

```graphql
query {
  operations {
    config {
      environmentName
      version
      logLevels { category level configuredLevel overridden }
    }
  }
}
```

`environmentName` is `IHostEnvironment.EnvironmentName` (`String!`).

`version` (`String!`) is the version of Trax serving the API, such as `1.46.0`: the Trax.Api.GraphQL
package's version without its build metadata, and what the operations console's footer shows. It
says which Trax release the host runs, not the host application's own
version, which Trax does not know.

`logLevels` is `[LogLevelSetting!]!`, one per category configured under `Logging:LogLevel` (and per
category changed at runtime since), `Default` first and the rest by category; empty when the host
configures none. It reads through `ILogLevelService` in Trax.Scheduler, the service the dashboard's
server settings call, so it includes a change made with
[`setLogLevels`](/docs/sdk-reference/graphql-api/mutations/config#setloglevels) or from the dashboard. Only
that section is read, so no other configuration value (a connection string, a secret) is reachable
from here. All three describe the API process, not the scheduler or worker processes.

### LogLevelSetting fields

| Field | Type | Description |
|-------|------|-------------|
| `category` | `String!` | The logging category, `Default` for the level every other category falls back to |
| `level` | `String!` | The level its loggers filter at now, by `LogLevel` name (`Trace` ... `None`): a level set at runtime when there is one, otherwise the configured one, `Information` when the configured value is not a level |
| `configuredLevel` | `String` | The value configured under `Logging:LogLevel`, as written; null when the category is not configured there |
| `overridden` | `Boolean!` | Whether a level was set at runtime, so `level` is not the configured one. It lasts until the process restarts |

A host that does not call `AddScheduler` registers no `ILogLevelService`: there `logLevels` reads
the configured section as written, with `overridden` always `false`, and `setLogLevels` refuses.

### SchedulerConfigSnapshot fields

| Field | Type | Description |
|-------|------|-------------|
| `manifestManagerEnabled` | `Boolean!` | Whether the manifest manager polling service runs |
| `jobDispatcherEnabled` | `Boolean!` | Whether the job dispatcher polling service runs |
| `manifestManagerPollingInterval` | `TimeSpan!` | How often the manifest manager polls |
| `jobDispatcherPollingInterval` | `TimeSpan!` | How often the job dispatcher polls |
| `maxActiveJobs` | `Int` | Global concurrency cap. Null means no cap |
| `defaultMaxRetries` | `Int!` | Default retry budget for new manifests |
| `failureCountWindow` | `TimeSpan!` | How far back failed runs count toward retry backoff and `MaxRetries` |
| `defaultRetryDelay` | `TimeSpan!` | First-retry delay |
| `retryBackoffMultiplier` | `Float!` | Exponential backoff factor |
| `maxRetryDelay` | `TimeSpan!` | Upper bound on backoff |
| `defaultJobTimeout` | `TimeSpan!` | Default per-execution timeout |
| `stalePendingTimeout` | `TimeSpan!` | When pending entries are reaped |
| `recoverStuckJobsOnStartup` | `Boolean!` | Whether stuck-job recovery runs on startup |
| `deadLetterRetentionPeriod` | `TimeSpan!` | How long resolved dead letters are kept before purging |
| `autoPurgeDeadLetters` | `Boolean!` | Whether the dead letter cleanup service runs |
| `localWorkerCount` | `Int` | In-process worker thread count. Null when the scheduler runs no local worker pool (no database provider, or a replaced job submitter). Local workers are on by default with a database provider; [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/configure-local-workers) tunes them |
| `metadataCleanupInterval` | `TimeSpan` | Metadata cleanup poll interval. Null when cleanup is not configured |
| `metadataCleanupRetention` | `TimeSpan` | How long completed metadata is kept. Null when cleanup is not configured |

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations.config mutations](/docs/sdk-reference/graphql-api/mutations/config)
