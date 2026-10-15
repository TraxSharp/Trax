---
title: Host tracking
description: "How every Metadata row records the host a train ran on: auto-detected Lambda, ECS, Kubernetes, Azure or server identity, the builder calls, columns and queries."
source:
  - Trax.Effect/src/Trax.Effect/Models/Host/TraxHostInfo.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxBuilder/TraxBuilder.Host.cs
type: reference
area: effects
nav_order: 2
---

# Host tracking

Every metadata row records the host the run executed on: its machine name, the kind of environment, an instance id and any labels the host adds. Hosts that share one database can be told apart by these columns.

## Behavior

At startup, `AddTrax()` detects the host environment and stores it process-wide (`TraxHostInfo.Current`). A run stamps that identity on its row when it moves to `InProgress`.

A row the dispatcher created before a remote worker ran it is stamped again when the worker starts the run, so the row names the host that executed it.

## Auto-Detection

Trax probes environment variables to determine where it's running:

| Environment | Detection | `HostEnvironment` | `HostInstanceId` |
|---|---|---|---|
| AWS Lambda | `AWS_LAMBDA_FUNCTION_NAME` | `"lambda"` | `AWS_LAMBDA_LOG_STREAM_NAME` |
| AWS ECS | `ECS_CONTAINER_METADATA_URI_V4` or `ECS_CONTAINER_METADATA_URI` | `"ecs"` | `HOSTNAME` (container ID) |
| Kubernetes | `KUBERNETES_SERVICE_HOST` | `"kubernetes"` | `HOSTNAME` (pod name) |
| Azure App Service | `WEBSITE_SITE_NAME` | `"azure-app-service"` | `WEBSITE_INSTANCE_ID` |
| Default | (none of the above) | `"server"` | `{MachineName}-{PID}` |

The variables are checked in the table's order, and the first one set wins. An instance id variable that is not set falls back to `{MachineName}-{PID}`. `HostName` is `Environment.MachineName`, or `unknown` when it cannot be read.

## Builder API

Host tracking needs no configuration. These calls on the `AddTrax` builder override the detected values or add labels:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .SetHostEnvironment("my-custom-env")          // Override auto-detected environment
    .SetHostInstanceId("worker-42")                // Override auto-detected instance ID
    .AddHostLabel("region", "us-east-1")           // Add a custom label
    .AddHostLabel("service", "content-shield")     // Add another label
    .AddHostLabels(new Dictionary<string, string>  // Add multiple labels at once
    {
        ["team"] = "platform",
        ["version"] = "2.1.0",
    })
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
);
```

Labels are stored as JSON in the `host_labels` column, or null when the host adds none.

## Database Columns

All columns are nullable and added to `trax.metadata`:

| Column | Type | Description |
|---|---|---|
| `host_name` | `varchar` | Machine hostname |
| `host_environment` | `varchar` | Environment type |
| `host_instance_id` | `varchar` | Instance-level identifier |
| `host_labels` | `jsonb` | User-provided key-value labels |

Postgres indexes `host_name` and `host_environment`, and `host_labels` with a GIN index over the rows that have labels, for containment queries.

## Querying

```sql
-- Find all trains that ran on Lambda
SELECT name, host_name, host_instance_id, train_state
FROM trax.metadata
WHERE host_environment = 'lambda'
ORDER BY id DESC LIMIT 20;

-- Find trains that ran in a specific region
SELECT name, host_environment, host_instance_id
FROM trax.metadata
WHERE host_labels @> '{"region": "us-east-1"}'
ORDER BY id DESC;

-- Group failures by host to find a bad instance
SELECT host_instance_id, COUNT(*) as failures
FROM trax.metadata
WHERE train_state = 'failed'
  AND start_time > NOW() - INTERVAL '1 hour'
GROUP BY host_instance_id
ORDER BY failures DESC;
```

## GraphQL

- The [`executions`](/docs/sdk-reference/graphql-api/queries/executions#executions) and [`executionDetail`](/docs/sdk-reference/graphql-api/queries/executions#executiondetail) queries under `operations` return `hostName`, `hostEnvironment` and `hostInstanceId`.
- [`hosts`](/docs/sdk-reference/graphql-api/queries/operations#hosts) lists the hosts that have run trains.
- The lifecycle subscriptions (`onTrainStarted`, `onTrainCompleted` and the rest) carry `hostName` and `hostEnvironment`.

## Dashboard

The metadata detail page shows an "Execution Host" card with the hostname, environment, instance id and labels. The metadata list has `Host Env` and `Host` columns, hidden until picked in the column picker.

## SDK Reference

> [Host Tracking](/docs/sdk-reference/configuration/host-tracking) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
