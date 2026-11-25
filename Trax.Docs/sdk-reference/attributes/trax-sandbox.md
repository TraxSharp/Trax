---
title: TraxSandbox
description: Reference for TraxSandbox, which declares that a train runs in a fresh sandbox per run, with the launcher profile and the CPU, memory, disk and time it may use.
type: reference
area: scheduling
package: Trax.Scheduler
namespace: Trax.Scheduler.Services.Sandbox
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/Sandbox/TraxSandboxAttribute.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/Sandbox/SandboxLimits.cs
parent: Attributes
grand_parent: SDK Reference
nav_order: 7
---

# TraxSandbox

Declares that a train runs in a sandbox of its own: a fresh, short-lived one per run (a Kata
microVM on a cluster, gVisor under podman on one host), started by the
[sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher), with no network and no
credentials. The sandbox entry point runs only trains that carry it, and a hub's
[`ITraxSandbox`](/docs/sdk-reference/scheduler-api/add-trax-sandbox) reads the profile and limits
from it. It ships in Trax.Scheduler, not Trax.Effect.

## Signature

```csharp fragment
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TraxSandboxAttribute(
    string profile,
    int cpuMillicores,
    int memoryMiB,
    int diskMiB,
    int timeoutSeconds
) : Attribute
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `profile` | `string` | The launcher profile to run in. The profile names the image, pinned by digest in the launcher's configuration; a request cannot name one. |
| `cpuMillicores` | `int` | CPU, in thousandths of a core: 1000 is one core. |
| `memoryMiB` | `int` | Memory, in MiB. The sandbox is killed when it goes over. |
| `diskMiB` | `int` | The size of the output volume, and of the scratch directory, in MiB each. A write past it fails with no space left. |
| `timeoutSeconds` | `int` | Wall-clock time from start, in seconds. The sandbox is killed when it runs over. |

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `Profile` | `string` | As passed. |
| `CpuMillicores`, `MemoryMiB`, `DiskMiB`, `TimeoutSeconds` | `int` | As passed. |
| `Limits` | `SandboxLimits` | The four limits as the launcher reads them. |

`SandboxLimits` is a record of the same four values. Its `Invalid()` returns why the limits
cannot run anywhere (a value that is not positive), or null.

## Behavior

- Every limit is required; none has a default. What the work needs is the train author's to say.
- The launcher refuses a run whose limits are over its host-wide caps, with the reason. It does
  not lower them.
- Put it on the train's class, not its interface. It is not inherited.
- A train without it is refused by the sandbox entry point even if a request names it, and
  `ITraxSandbox.RunAsync` throws before sending anything.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.Sandbox;

public sealed record BuildInput(string Source);

public sealed record BuildOutput(int Warnings);

public interface IBuildTrain : IServiceTrain<BuildInput, BuildOutput>;

[TraxSandbox("builder", cpuMillicores: 2000, memoryMiB: 1024, diskMiB: 256, timeoutSeconds: 600)]
public sealed class BuildTrain : ServiceTrain<BuildInput, BuildOutput>, IBuildTrain
{
    protected override Task<Either<Exception, BuildOutput>> Junctions() =>
        Chain<CompileJunction>().Resolve();
}

public sealed class CompileJunction(SandboxWorkspace workspace) : EffectJunction<BuildInput, BuildOutput>
{
    public override async Task<BuildOutput> Run(BuildInput input)
    {
        await File.WriteAllTextAsync(Path.Combine(workspace.FilesDirectory, "build.log"), input.Source);
        return new BuildOutput(Warnings: 0);
    }
}
```

## See also

- [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox)
- [AddTraxSandbox](/docs/sdk-reference/scheduler-api/add-trax-sandbox), the hub's client and the entry point inside the sandbox
- [Sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher)
- [Sandbox threat model](/docs/security/sandbox-threat-model)
