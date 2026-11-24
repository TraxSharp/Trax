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

## 4. Deploy the launcher on one host

The launcher is its own process: a console app whose `Program.cs` is one line,
`await TraxSandboxLauncher.RunAsync(args);`, with `Trax.Scheduler.Sandbox.Launcher` referenced.

1. Install Kata Containers and add it to podman's runtimes in `containers.conf`
   (`[engine.runtimes] kata = ["/usr/bin/kata-runtime"]`). This needs `/dev/kvm`.
2. Create a user for the launcher, enable its rootless podman socket
   (`systemctl --user enable --now podman.socket` as that user), and pull the sandbox and holder
   images as that user.
3. Configure it under `Trax:SandboxLauncher`: `ListenSocketPath`, the `SigningKey`, a profile
   with the image's digest, `Podman:SocketPath` and `Podman:HolderImage` (a pause image, by digest).
   Every option is on the [launcher's page](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher).
4. Run it as a systemd service of that user. It refuses to start until its canary has shown a
   sandbox on a guest kernel with nothing reachable; its log says why when it refuses.
5. Mount the launcher's socket (not podman's) into the hub's container, with the same key in the
   hub's configuration. Keep the socket's directory readable only by the launcher's user and a group
   the hub runs in.

Never mount podman's socket into the hub or a sandbox.

## 5. Deploy the launcher on a cluster

1. Give the cluster a Kata RuntimeClass (AKS offers one; EKS needs nodes set up for it). Where only
   gVisor exists (GKE Sandbox), set `Runtime` to `GVisor` and `Kubernetes:RuntimeClassName` to its
   class: the launcher accepts gVisor only by name.
2. Make sure the network plugin enforces NetworkPolicy. The canary refuses to start otherwise,
   since sandboxes are kept off the network by a deny-all policy.
3. Render the objects with `KubernetesSandboxManifests.Render(options, launcherImage, hubNamespace)`
   and apply them, with a Secret named `trax-sandbox-launcher-key` holding the key as `signing-key`.
   The admission policies are cluster-scoped, so applying them needs a cluster admin.
4. Point the hub at the launcher's Service: `LauncherUrl = "https://trax-sandbox-launcher.trax-sandbox:8080"`
   behind TLS, or `http://` with `AllowInsecureTransport` where the cluster network is trusted.
   Answers are signed either way; the input and output are not encrypted without TLS.
5. On EKS, create no Pod Identity association and no IRSA annotation for anything in the sandbox
   namespace.

## Proving it on your host

The escape suite (`PodmanEscapeTests` in `Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Tests`) runs
hostile trains against a real launcher and podman. It needs a podman socket, so it skips in CI and
under `trax-test`. To run it, build the image with
`Trax.Scheduler/tests/Trax.Scheduler.Sandbox.Image/build-escape-image.sh`, which prints
`TRAX_SANDBOX_IMAGE` and `TRAX_SANDBOX_HOLDER_IMAGE`, and run the suite in a container with a podman
socket mounted at `TRAX_SANDBOX_PODMAN_SOCKET`. With Kata installed it runs under `kata`; without it,
`TRAX_SANDBOX_OCI_RUNTIME=crun` and `TRAX_SANDBOX_ASSUME_ISOLATED=1` prove the mechanics only.

## SDK Reference

> [TraxSandbox](/docs/sdk-reference/attributes/trax-sandbox) | [AddTraxSandbox](/docs/sdk-reference/scheduler-api/add-trax-sandbox) | [Sandbox launcher](/docs/sdk-reference/scheduler-api/trax-sandbox-launcher)
