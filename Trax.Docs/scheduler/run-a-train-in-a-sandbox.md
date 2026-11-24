---
title: Run a train in a sandbox
description: "Declare a train that runs untrusted work in a fresh Kata sandbox per run, call it from a hub, and deploy the launcher on one host or a cluster."
type: how-to
area: scheduling
nav_order: 32
---

# Run a train in a sandbox

This runs a train whose work you do not trust, such as building or running code an agent wrote, in
a fresh microVM per run with no network and no credentials. The hub hands the input to a separate
process, the sandbox launcher, which is the only thing that can start a sandbox; the output comes
back through the launcher once the sandbox has exited. Why it is built this way, and what it does
and does not protect against, is in the [sandbox threat model](/docs/security/sandbox-threat-model).

## 1. Declare the train

Mark the train's class with [`[TraxSandbox]`](/docs/sdk-reference/attributes/trax-sandbox), naming
the launcher profile it runs in and every limit. Its junctions inject `SandboxWorkspace` to find
where to write the files the hub should get back:

```csharp compile=train
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
        await File.WriteAllTextAsync(Path.Combine(workspace.FilesDirectory, "build.log"), input.Source, CancellationToken);
        return new BuildOutput(Warnings: 0);
    }
}
```

Put the train in a library both the hub and the sandbox image reference: the hub reads the
attribute from it, the image runs it.

## 2. Build the sandbox image

The image's entry point is a host with Trax on an in-memory provider, the mediator over the train's
assembly, and nothing else: no database, broker, secret or cloud credential.

```csharp compile=image
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var sandbox = new ServiceCollection()
    .AddLogging()
    .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(Program).Assembly))
    .AddTraxSandboxRunner()
    .BuildServiceProvider();

return await sandbox.RunTraxSandboxAsync();
```

Build it on a runtime image, run it as a non-root user, and push it. The launcher runs it by
digest, so note the digest the registry gives back.

## 3. Call it from the hub

Register the client and inject `ITraxSandbox` into the hub junction that hands work out:

```csharp compile=hub
using Trax.Scheduler.Extensions;

services.AddTraxSandbox(sandbox =>
{
    sandbox.LauncherSocketPath = "/run/trax-sandbox/launcher.sock";
    sandbox.SigningKey = Convert.FromBase64String(configuration["Trax:SandboxKey"] ?? "");
});
```

`ITraxSandbox.RunAsync<IBuildTrain, BuildInput, BuildOutput>(input)` returns the output and the
files. A run that is refused, busy, killed for its timeout or memory, or whose output is refused
throws `SandboxRunException` with the reason, which fails the hub's run with it. The options are on
[AddTraxSandbox](/docs/sdk-reference/scheduler-api/add-trax-sandbox).

## 4. Deploy the launcher

The launcher is its own process: a console app whose `Program.cs` is one line,
`await TraxSandboxLauncher.RunAsync(args);`, with `Trax.Scheduler.Sandbox.Launcher` referenced, built
into an image of its own.

Kata runs only through Kubernetes. Kata Containers 4 has no OCI command line, so podman cannot run
it at all; one machine with Kata is k3s with a Kata RuntimeClass and the launcher's Kubernetes
backend, the same as any cluster. The steps are the same on k3s, AKS, or EKS nodes set up for Kata.

1. Give the cluster a Kata RuntimeClass. On k3s, install Kata (`kata-deploy`, or the static release
   plus a containerd `config-v3.toml.tmpl` that registers its shim) on a machine with `/dev/kvm`.
   Where only gVisor exists (GKE Sandbox), set `Runtime` to `GVisor` and
   `Kubernetes:RuntimeClassName` to its class: the launcher accepts gVisor only by name.
2. Make sure the network plugin enforces NetworkPolicy (k3s's built-in one does). The canary refuses
   to start otherwise, since sandboxes are kept off the network by a deny-all policy.
3. Have the launcher image write the objects the namespace needs, from the same configuration it
   will run with:

   ```bash
   docker run --rm -v "$PWD:/out" \
     -e Trax__SandboxLauncher__Profiles__builder__Image=registry.example.com/builder@sha256:<digest> \
     -e Trax__SandboxLauncher__CanaryProbes__0=<hub address>:<port> \
     registry.example.com/launcher:1 \
     --trax-sandbox-manifests /out/sandbox.yaml \
     --launcher-image registry.example.com/launcher@sha256:<digest> \
     --hub-namespace trax-hub
   ```

   Every `Trax__SandboxLauncher__...` setting it is given (caps, probes, runtime class) is rendered
   into the launcher's Deployment, so the launcher and the admission rules cannot disagree. Code
   can call `KubernetesSandboxManifests.Render` instead.
4. Apply it, and a Secret named `trax-sandbox-launcher-key` holding the key as `signing-key`. The
   admission policies are cluster-scoped, so applying them needs a cluster admin; they come first in
   the file, so the launcher's own pod is admitted under them.
5. The launcher listens on plain HTTP on port 8080, behind a NetworkPolicy that admits only the
   hub's namespace. Point the hub at `http://trax-sandbox-launcher.trax-sandbox:8080` with
   `AllowInsecureTransport`, or put TLS in front of it. Answers are signed either way; the input and
   output are not encrypted without TLS.
6. On EKS, create no Pod Identity association and no IRSA annotation for anything in the sandbox
   namespace.

The launcher does not listen until its canary has run a sandbox and seen a kernel other than the
node's, and nothing it could reach; its log says why when it refuses.

### Podman on one host

The podman backend runs sandboxes through a rootless podman socket only the launcher holds. Podman
cannot run Kata, so it is only as isolating as gVisor (`Runtime: GVisor`, with `runsc` in podman's
runtimes); with `Kata` configured it refuses to start and says so. Set `ListenSocketPath`,
`Podman:SocketPath` and `Podman:HolderImage` (a pause image, by digest), run it as a user of its own,
and mount the launcher's socket (never podman's) into the hub's container.

## Proving it on your host

The escape suite (`PodmanEscapeTests` in `Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Tests`) runs
hostile trains against a real launcher and podman; `KubernetesEscapeTests` runs the same against a
launcher on a cluster. They need a podman socket or a cluster, so they skip in CI and under `trax-test`. To run it, build the image with
`Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Image/build-escape-image.sh`, which prints
`TRAX_SANDBOX_IMAGE` and `TRAX_SANDBOX_HOLDER_IMAGE`, and run the suite in a container with a podman
socket mounted at `TRAX_SANDBOX_PODMAN_SOCKET`; podman cannot run Kata, so
`TRAX_SANDBOX_OCI_RUNTIME=crun` and `TRAX_SANDBOX_ASSUME_ISOLATED=1` prove the mechanics only. For a
cluster, deploy as in step 4 with the escape image as the profile, port-forward the launcher's
Service, and set `TRAX_SANDBOX_LAUNCHER_URL`, `TRAX_SANDBOX_KEY`, `TRAX_SANDBOX_FORBIDDEN` (the hub,
the database, the API server, the launcher) and `TRAX_SANDBOX_MAX_CONCURRENT`.

## SDK Reference

> [TraxSandbox](/docs/sdk-reference/attributes/trax-sandbox) | [AddTraxSandbox](/docs/sdk-reference/scheduler-api/add-trax-sandbox) | [Sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher)
