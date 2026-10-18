---
title: Host Tracking
description: "Reference for the host identity methods on TraxBuilder: SetHostEnvironment, SetHostInstanceId, AddHostLabel and AddHostLabels, and the TraxHostInfo they build."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 3
package: Trax.Effect
namespace:
  - Trax.Effect.Configuration.TraxBuilder
  - Trax.Effect.Models.Host
  - Trax.Effect.Models.HostHeartbeat
source:
  - Trax.Effect/src/Trax.Effect/Configuration/TraxBuilder/TraxBuilder.Host.cs
  - Trax.Effect/src/Trax.Effect/Models/Host/TraxHostInfo.cs
  - Trax.Effect/src/Trax.Effect/Models/HostHeartbeat/HostHeartbeat.cs
---

# Host Tracking

The methods on `TraxBuilder` that override or extend the host identity Trax stamps on every run, and `TraxHostInfo`,
the identity they build. All are optional: without them the identity is auto-detected.

## Signatures

```csharp fragment
public TraxBuilder SetHostEnvironment(string environment)
public TraxBuilder SetHostInstanceId(string instanceId)
public TraxBuilder AddHostLabel(string key, string value)
public TraxBuilder AddHostLabels(Dictionary<string, string> labels)
```

```csharp fragment
namespace Trax.Effect.Models.Host;

public record TraxHostInfo
{
    public string HostName { get; init; }
    public string HostEnvironment { get; init; }
    public string HostInstanceId { get; init; }
    public Dictionary<string, string> Labels { get; init; }

    public static TraxHostInfo? Current { get; }
    public static TraxHostInfo AutoDetect();
}
```

## Parameters

| Method | Parameter | Description |
|--------|-----------|-------------|
| `SetHostEnvironment` | `environment` | Replaces the detected environment, such as `"lambda"`, `"ecs"` or `"my-custom-env"` |
| `SetHostInstanceId` | `instanceId` | Replaces the detected instance id |
| `AddHostLabel` | `key`, `value` | Adds one label, such as `"region"` = `"us-east-1"` |
| `AddHostLabels` | `labels` | Adds every pair in the dictionary |

Each throws `ArgumentNullException` for a null argument, and returns the builder for chaining.

## Members

| Member | Description |
|--------|-------------|
| `HostName` | The machine name, or `unknown` when it cannot be read |
| `HostEnvironment` | `lambda`, `ecs`, `kubernetes`, `azure-app-service` or `server`, unless `SetHostEnvironment` replaced it |
| `HostInstanceId` | The Lambda log stream, the ECS or Kubernetes `HOSTNAME`, the Azure `WEBSITE_INSTANCE_ID`, or `{MachineName}-{ProcessId}`, unless `SetHostInstanceId` replaced it |
| `Labels` | The labels added with `AddHostLabel` and `AddHostLabels`, stored as JSONB on every metadata row |
| `Current` | The identity `AddTrax` built, or `null` before `AddTrax` runs. Process-wide: a second `AddTrax` in the same process (two test hosts, say) replaces it. |
| `AutoDetect()` | Probes the environment and returns a new identity with no labels. It does not change `Current`. |

## Behavior

[AddTrax](/docs/sdk-reference/configuration/add-trax) builds the identity once, after its callback runs: it
auto-detects, applies the overrides, and merges the labels. A label key added twice keeps the last value. Every
run in the process is stamped with it when it starts, so a run executed by a remote worker carries the worker's
identity, not the dispatcher's.

## Heartbeat

A host with a data provider also writes that identity to `trax.host_heartbeat` when it starts and every 15 seconds
after, whether or not it has work, so an idle host can be told from a dead one. The row is the model
`HostHeartbeat`, read through `IDataContext.HostHeartbeats`:

| Member | Description |
|--------|-------------|
| `InstanceId` | `HostInstanceId`, the primary key: one row per process |
| `HostName`, `HostEnvironment` | As above |
| `TraxVersion` | The Trax version the process runs |
| `StartedAt` | When the process began beating |
| `LastHeartbeatAt` | When the process last wrote the row |
| `RunsDispatcher` | Whether the process runs the scheduler's job dispatcher, switched on |
| `RunsManifestManager` | Whether the process runs the scheduler's manifest manager, switched on |
| `WorkerSlots` | How many local workers the process runs, or `null` with none |
| `WorkerSlotsInUse` | How many of them were running a job at the last beat, or `null` with none |

A host is offline once its last beat is 45 seconds old, three missed beats, and any host's beat deletes the rows whose
last beat is a day old. [`operations.hosts`](/docs/sdk-reference/graphql-api/queries/operations#hosts) reads the
table. The migration is described in [Schema migrations](/docs/reference/schema-migrations).

## Example

```csharp compile
services.AddTrax(trax => trax
    .SetHostEnvironment("ecs")
    .AddHostLabel("region", "us-east-1")
    .AddHostLabel("service", "content-shield")
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Host Tracking](/docs/effect/host-tracking), the detection table and how to query by host
- [AddTrax](/docs/sdk-reference/configuration/add-trax)
