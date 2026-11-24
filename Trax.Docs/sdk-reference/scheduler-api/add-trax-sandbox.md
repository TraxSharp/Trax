---
title: AddTraxSandbox
description: Reference for AddTraxSandbox and ITraxSandbox, which run a TraxSandbox train through the launcher, and AddTraxSandboxRunner, the entry point inside it.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 16
package: Trax.Scheduler
namespace:
  - Trax.Scheduler.Extensions
  - Trax.Scheduler.Configuration
  - Trax.Scheduler.Services.Sandbox
source:
  - Trax.Scheduler/src/Trax.Scheduler/Extensions/SandboxExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SandboxClientOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Services/Sandbox
---

# AddTraxSandbox

Registers `ITraxSandbox` on a hub: the client that hands a [`[TraxSandbox]`](/docs/sdk-reference/attributes/trax-sandbox)
train's input to the [sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher),
waits while it runs in a fresh sandbox, and returns its output and files. The same package holds
the other end, `AddTraxSandboxRunner` and `RunTraxSandboxAsync`, which a sandbox image calls in
place of starting its host.

## Signatures

```csharp fragment
public static IServiceCollection AddTraxSandbox(
    this IServiceCollection services,
    Action<SandboxClientOptions> configure
)

public static IServiceCollection AddTraxSandboxRunner(
    this IServiceCollection services,
    string root = "/sandbox",
    string scratch = "/tmp"
)

public static Task<int> RunTraxSandboxAsync(
    this IServiceProvider services,
    CancellationToken ct = default
)

public interface ITraxSandbox
{
    Task<SandboxRun<TOutput>> RunAsync<TTrain, TInput, TOutput>(TInput input, CancellationToken ct = default)
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : notnull;
}
```

## Parameters

### SandboxClientOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LauncherSocketPath` | `string?` | | The launcher's Unix socket, on one host. Set this or `LauncherUrl`, not both. |
| `LauncherUrl` | `string?` | | The launcher's base URL, on a cluster. `https://` unless its host is loopback or `AllowInsecureTransport` is set. |
| `AllowInsecureTransport` | `bool` | `false` | Accepts `http://` to a host that is not loopback. Answers are signed either way; the input and output cross in cleartext. |
| `SigningKey` | `byte[]?` | | The key shared with the launcher, at least 32 bytes. Required. |
| `MaxResponseBodyBytes` | `long` | `DefaultMaxResponseBodyBytes` (128 MiB) | The most bytes of an answer read; a larger one fails the run unread. |
| `Archive` | `SandboxArchiveLimits` | 64 MiB, 10000 entries | The caps the hub checks the returned files against again. Keep them at or above the launcher's. |
| `ResponseGrace` | `TimeSpan` | 2 minutes | How long past the train's own timeout the hub waits, for starting and collecting the sandbox. |

`Validate()` throws `InvalidOperationException` for no launcher or two, no key or a short one, a
relative socket path, a cleartext URL off loopback, or a cap that is not positive.
`AddTraxSandbox` calls it, so a hub with a bad setting does not start.

### RunAsync

| Parameter | Description |
|-----------|-------------|
| `TTrain` | The train's interface. It is registered on the hub as well as in the profile's image, so the hub can read its class's `[TraxSandbox]`. |
| `input` | The input. It is the only thing from the hub that reaches the sandbox. |
| `ct` | Cancels the wait. The launcher still kills the sandbox at its timeout. |

## Returns

`SandboxRun<TOutput>`: `Output`, the train's output, and `Files`, the `SandboxFile`s (`Path`,
relative with `/` separators, and `Content`) the train left in `SandboxWorkspace.FilesDirectory`,
checked by the launcher and again by the hub.

## Behavior

- The request carries the run id (32 random hex characters), the profile and the limits from the
  train's class, the train's interface name and the input as JSON. Nothing else: no image, command,
  environment, mount or runtime. See [The wire](#the-wire).
- The answer is read only when it carries the launcher's signature for this request; any other
  answer, whatever its status code, fails the run.
- A run that did not complete throws `SandboxRunException`, a `TrainException` whose `Outcome` says
  how it ended and whose message says why, naming the train and the run id. Thrown from a hub
  junction, it fails the hub's run with that reason. Its message is the launcher's, not a train
  author's, so a client is not shown it.
- A train that ran and failed inside the sandbox throws the failure it recorded, rebuilt as a
  `RemoteRunException` the way a [remote run](/docs/scheduler/remote-error-responses) is, with the
  train author's own message as its public message.
- A train whose class does not carry `[TraxSandbox]`, or that is not registered on the hub, throws
  `InvalidOperationException` before anything is sent.

### Registered services

| Service | Lifetime | By |
|---------|----------|----|
| `ITraxSandbox` | Scoped | `AddTraxSandbox` |
| A named `HttpClient` over the socket or URL, with no proxy and no redirects | | `AddTraxSandbox` |
| `SandboxWorkspace` | Singleton | `AddTraxSandboxRunner` |
| The entry point `RunTraxSandboxAsync` calls | Singleton | `AddTraxSandboxRunner` |

## Inside the sandbox

The sandbox image's host registers Trax with an in-memory data provider and the mediator, and no
database, broker or cloud credential, then calls `AddTraxSandboxRunner` and returns
`RunTraxSandboxAsync`'s exit code instead of running a host:

- It reads `request.json` from the input directory, runs the train it names, by its exact interface
  name and only when its class carries `[TraxSandbox]`, and writes `result.json` (a
  `RemoteRunResponse`) to the output directory. It returns 0 once a result is written, the train's
  own failure included, and 2 when the request is missing or unreadable.
- With `canary.json` in the input directory instead, it runs no train: it writes
  `canary-report.json`, a `SandboxCanaryReport` of the `Kernel` it sees in `/proc/version`, its
  network `Interfaces`, and which of the `SandboxCanaryRequest`'s `Probes` it `Reached`.

`SandboxWorkspace` is what a junction injects to find its directories: `InputDirectory` (read-only),
`OutputDirectory`, `FilesDirectory` (under the output; only these files go back) and
`ScratchDirectory` (a size-capped tmpfs, gone with the sandbox). `SandboxPaths` names them:
`Root` (`/sandbox`), `Input` (`in`), `Output` (`out`), `Files` (`files`), `Scratch` (`/tmp`),
`RequestFile`, `CanaryFile`, `ResultFile` and `CanaryReportFile`.

## The wire

These types are the contract between the hub, the launcher and the sandbox, which ship in two
packages. They are public for that, and hidden from IntelliSense.

| Type | Members |
|------|---------|
| `SandboxRunRequest` | `RunId`, `Profile`, `TrainName`, `InputJson`, `Limits` (a `SandboxLimits`: `CpuMillicores`, `MemoryMiB`, `DiskMiB`, `TimeoutSeconds`) |
| `SandboxRunResponse` | `RunId`, `Outcome`, `Reason`, `ResultJson` (the sandbox's own result, untrusted), `Archive` (a base64 tar the launcher wrote) |
| `SandboxOutcome` | `Completed`, `Refused`, `Busy`, `TimedOut`, `OutOfMemory`, `Exited`, `OutputRefused`, `Failed` |
| `SandboxJson` | `Options`: web defaults, enums by name, and any member it does not know refused, which is what keeps the request's shape fixed |
| `SandboxChannelSignature` | `HeaderName` (`Trax-Sandbox-Signature`) and `ResponseHeaderName` (`Trax-Sandbox-Response-Signature`); `MinimumKeyLength` (32) and `EnsureKey`; `Sign` and `TryVerify` for a request, `TryReadTimestamp` to refuse a stale one unread; `SignResponse` and `VerifyResponse` for an answer bound to its request's nonce |
| `SandboxArchive` | `ReadAsync`: reads a tar without extracting it, refusing it whole for a link, a device, a named pipe, a path out of its directory, a duplicate, or more than its `SandboxArchiveLimits`; `WriteAsync`: a tar of regular files that depends on nothing but the files |
| `SandboxArchiveLimits` | `MaxBytes`, `MaxFiles`, `MaxPathLength` (1024), `OverheadPerEntry` and `MaxRawBytes`, the most raw tar bytes read |

A request's signature is `v1,t=<unix seconds>,n=<nonce>,s=<base64 HMAC-SHA256>` over the timestamp,
nonce and exact body; an answer's is `v1,s=<base64>` over the request's nonce and the exact answer
body. The prefixes differ from each other and from the [runner signature](/docs/scheduler/runner-authorization),
so none can stand in for another.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Sandbox;

services.AddTraxSandbox(sandbox =>
{
    sandbox.LauncherSocketPath = "/run/trax-sandbox/launcher.sock";
    sandbox.SigningKey = Convert.FromBase64String(configuration["Trax:SandboxKey"] ?? "");
});

public sealed record BuildInput(string Source);

public sealed record BuildOutput(int Warnings);

public interface IBuildTrain : IServiceTrain<BuildInput, BuildOutput>;

// On the hub: a junction that hands the build to a sandbox and keeps its log.
public sealed class BuildInSandbox(ITraxSandbox sandbox) : EffectJunction<BuildInput, BuildOutput>
{
    public override async Task<BuildOutput> Run(BuildInput input)
    {
        var run = await sandbox.RunAsync<IBuildTrain, BuildInput, BuildOutput>(input, CancellationToken);
        var log = run.Files.FirstOrDefault(f => f.Path == "build.log");
        return run.Output ?? new BuildOutput(Warnings: log is null ? -1 : 0);
    }
}
```

## See also

- [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox)
- [TraxSandbox](/docs/sdk-reference/attributes/trax-sandbox)
- [Sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher)
- [Sandbox threat model](/docs/security/sandbox-threat-model)
