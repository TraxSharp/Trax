---
layout: default
title: Host Tracking
description: "Reference for the host identity methods on TraxBuilder: SetHostEnvironment, SetHostInstanceId, AddHostLabel and AddHostLabels, and the TraxHostInfo they build."
parent: Configuration
grand_parent: SDK Reference
nav_order: 11
---

# Host Tracking

Methods on `TraxBuilder` for configuring host identity. All are optional. Host tracking works with zero configuration via auto-detection.

## SetHostEnvironment

Overrides the auto-detected host environment type.

```csharp
public TraxBuilder SetHostEnvironment(string environment)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `environment` | `string` | Yes | Environment identifier (e.g., `"lambda"`, `"ecs"`, `"my-custom-env"`) |

## SetHostInstanceId

Overrides the auto-detected host instance ID.

```csharp
public TraxBuilder SetHostInstanceId(string instanceId)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `instanceId` | `string` | Yes | Instance identifier |

## AddHostLabel

Adds a custom key-value label to the host identity. Labels are stored as JSONB on every metadata record.

```csharp
public TraxBuilder AddHostLabel(string key, string value)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `key` | `string` | Yes | Label key (e.g., `"region"`, `"service"`) |
| `value` | `string` | Yes | Label value (e.g., `"us-east-1"`) |

## AddHostLabels

Adds multiple custom labels at once.

```csharp
public TraxBuilder AddHostLabels(Dictionary<string, string> labels)
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `labels` | `Dictionary<string, string>` | Yes | Key-value pairs to add |

## Example

```csharp
services.AddTrax(trax => trax
    .SetHostEnvironment("ecs")
    .AddHostLabel("region", "us-east-1")
    .AddHostLabel("service", "content-shield")
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
);
```

## TraxHostInfo

The identity these methods build, readable from code.

```csharp
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

| Member | Description |
|--------|-------------|
| `HostName` | The machine name, or `unknown` when it cannot be read |
| `HostEnvironment` | `lambda`, `ecs`, `kubernetes`, `azure-app-service` or `server`, unless `SetHostEnvironment` overrode it |
| `HostInstanceId` | The Lambda log stream, the ECS or Kubernetes `HOSTNAME`, the Azure `WEBSITE_INSTANCE_ID`, or `{MachineName}-{ProcessId}`, unless `SetHostInstanceId` overrode it |
| `Labels` | The labels added with `AddHostLabel` and `AddHostLabels` |
| `Current` | The identity `AddTrax` built, or `null` before `AddTrax` runs. Process-wide: a second `AddTrax` in the same process (two test hosts, say) replaces it. |
| `AutoDetect()` | Probes the environment and returns a new identity with no labels. It does not change `Current`. |

## Remarks

- Host identity is detected once at startup and applied to all train executions in the process.
- Auto-detection probes environment variables for Lambda, ECS, Kubernetes, and Azure App Service. See [Host Tracking](/docs/effect/host-tracking) for the full detection table.
- Duplicate label keys use last-write-wins semantics.
- When a remote worker executes a train, host fields are overwritten when the run starts, so the metadata reflects the actual execution host, not the dispatcher.
