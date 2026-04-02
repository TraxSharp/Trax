---
layout: default
title: Host Tracking
parent: Effect
nav_order: 2
---

# Host Tracking

When trains run across distributed infrastructure (Lambda functions, ECS tasks, Kubernetes pods, multiple servers), every metadata record captures WHERE the train executed. Since all hosts share the same Postgres database, you can query exactly which machine ran any given train.

## How It Works

At startup, `AddTrax()` auto-detects the host environment and stores it as a process-wide singleton (`TraxHostInfo.Current`). Every `Metadata` record is stamped with this identity when the train starts executing.

For remote workers: the metadata row is initially created by the dispatcher, but when the worker picks up the job and calls `StartServiceTrain()`, the host fields are overwritten with the actual execution host's identity.

## Auto-Detection

Trax probes environment variables to determine where it's running:

| Environment | Detection | `HostEnvironment` | `HostInstanceId` |
|---|---|---|---|
| AWS Lambda | `AWS_LAMBDA_FUNCTION_NAME` | `"lambda"` | `AWS_LAMBDA_LOG_STREAM_NAME` |
| AWS ECS | `ECS_CONTAINER_METADATA_URI_V4` | `"ecs"` | `HOSTNAME` (container ID) |
| Kubernetes | `KUBERNETES_SERVICE_HOST` | `"kubernetes"` | `HOSTNAME` (pod name) |
| Azure App Service | `WEBSITE_SITE_NAME` | `"azure-app-service"` | `WEBSITE_INSTANCE_ID` |
| Default | (none of the above) | `"server"` | `{MachineName}-{PID}` |

`HostName` is always `Environment.MachineName`.

## Builder API

Host tracking works with zero configuration. To override or add labels:

```csharp
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

Labels are stored as JSONB in the `host_labels` column and can be queried with PostgreSQL containment operators.

## Database Columns

All columns are nullable and added to `trax.metadata`:

| Column | Type | Description |
|---|---|---|
| `host_name` | `varchar` | Machine hostname |
| `host_environment` | `varchar` | Environment type |
| `host_instance_id` | `varchar` | Instance-level identifier |
| `host_labels` | `jsonb` | User-provided key-value labels |

Indexes: `host_name`, `host_environment`, and a GIN index on `host_labels` for containment queries.

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

Host fields are available on both queries and subscriptions:

- `getExecutions` / `getExecution`: returns `hostName`, `hostEnvironment`, `hostInstanceId`
- Lifecycle subscriptions (`onTrainStarted`, `onTrainCompleted`, etc.): include `hostName`, `hostEnvironment`

## Dashboard

The metadata detail page shows an "Execution Host" card with hostname, environment, instance ID, and labels. The metadata list page includes optional (hidden by default) `Host Env` and `Host` columns that can be enabled via the column picker.

## SDK Reference

> [Host Tracking](/docs/sdk-reference/configuration/host-tracking) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus)
