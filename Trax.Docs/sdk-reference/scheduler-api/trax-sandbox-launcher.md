---
title: Sandbox launcher
description: Reference for TraxSandboxLauncher, the one process that starts sandboxes, its options, and the cluster objects KubernetesSandboxManifests renders.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 17
package: Trax.Scheduler.Sandbox.Launcher
namespace:
  - Trax.Scheduler.Sandbox.Launcher
  - Trax.Scheduler.Sandbox.Launcher.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler.Sandbox.Launcher/TraxSandboxLauncher.cs
  - Trax.Scheduler/src/Trax.Scheduler.Sandbox.Launcher/KubernetesSandboxManifests.cs
  - Trax.Scheduler/src/Trax.Scheduler.Sandbox.Launcher/Configuration
---

# Sandbox launcher

The only process that can start a sandbox. It runs as its own deployment, listens on a Unix socket
(one host) or a cluster-internal port, and takes one request shape from a hub
([`AddTraxSandbox`](/docs/sdk-reference/scheduler-api/add-trax-sandbox)): profile, train, input and
limits. It starts a fresh sandbox under Kata Containers, passes the input in, waits, kills the
sandbox at its timeout, reads its output volume after it exits, checks it, hands it back, and
removes everything the run made.

## Signatures

```csharp fragment
public static class TraxSandboxLauncher
{
    public static Task RunAsync(string[] args, CancellationToken ct = default);
    public static WebApplication Build(string[] args);
}

public static class KubernetesSandboxManifests
{
    public static string Render(
        SandboxLauncherOptions options,
        string launcherImage,
        string hubNamespace,
        string runsStorage = "20Gi"
    );
}
```

## Parameters

Everything is read from configuration under `Trax:SandboxLauncher` (`SandboxLauncherOptions.Section`)
by `SandboxLauncherOptions.Read`, which refuses a setting it does not know, so a misspelt cap is an
error rather than a default left in place.

### SandboxLauncherOptions

| Property | Default | Description |
|----------|---------|-------------|
| `Runtime` | `Kata` | A `SandboxRuntime`: `Kata`, a microVM per sandbox, through a RuntimeClass only; or `GVisor`, only by naming it, for a cluster that offers nothing else or for podman. |
| `Backend` | `Podman` | A `SandboxBackendKind`: `Podman` on one host (gVisor only), `Kubernetes` on a cluster, k3s on one machine included (the only way to Kata). |
| `SigningKey` | | The key shared with the hub, base64 in configuration, at least 32 bytes. |
| `ListenSocketPath` | | The Unix socket to listen on, on one host, ending in `.sock`. Set this or `ListenUrl`. An empty file left at the path by an earlier launcher is removed first; anything else is left, and the start fails. |
| `ListenSocketMode` | `0660` | The socket's permissions, set once it listens. |
| `ListenUrl` | | `http://<IP address>:<port>` to listen on, on a cluster. |
| `Profiles` | | By name (lowercase letters, digits, `-`): each a `SandboxProfile` with `Image` (`name@sha256:<digest>`; a tag alone is refused), an optional `Command`, and `User` (`uid:gid`, never 0, `65532:65532` by default). |
| `Caps` | | A `SandboxCaps`, below. |
| `Podman` | | A `PodmanBackendOptions`: `SocketPath`, the podman API socket only the launcher holds; `HolderImage`, pinned by digest; `HolderCommand`, when the holder image's own command does not block. |
| `Kubernetes` | | A `KubernetesBackendOptions`: `Namespace` (`trax-sandbox`), `RuntimeClassName` (`kata`), `RunsClaimName` (`trax-sandbox-runs`), `RunsPath` (`/runs`), `NodeName` (from `NODE_NAME` when not set), `PollInterval` (2 seconds) and `RunsMinFreePercent` (15, the share of the runs volume's filesystem that must stay free). |
| `CanaryProbes` | | More `host:port` pairs the startup canary must fail to reach: the hub, the database. |

`Validate()` throws `InvalidOperationException`, naming the first problem, for a missing or short
key, no listener or two, a socket path not ending in `.sock`, a listen URL that is not an address and a port, no profile, an unpinned image, a root user, an unpinned holder, a relative
socket, a cluster backend with no node name, a runs path at the root or a `RunsMinFreePercent` outside 1 to 90, or a cap that is not positive.

### SandboxCaps

The host-wide caps the launcher never goes over. A request over one is refused with the reason, not
lowered.

| Property | Default |
|----------|---------|
| `MaxCpuMillicores` | 4000 |
| `MaxMemoryMiB` | 4096 |
| `MaxDiskMiB` | 2048 |
| `MaxTimeoutSeconds` | 3600 |
| `MaxConcurrent` | 4; a request past it is answered `Busy` at once, not queued |
| `MaxPids` | 512 processes and threads per sandbox, which holds a fork bomb, on podman. A cluster applies its kubelet's `podPidsLimit` instead; under Kata the VM's own memory and the timeout hold one |
| `MaxInputBytes` | 4 MiB of input JSON |
| `MaxResultBytes` | 16 MiB of result file |
| `MaxArchiveBytes` | 64 MiB of returned files, uncompressed |
| `MaxArchiveFiles` | 10000 returned files and directories |

## Behavior

On start, before it listens:

1. It refuses any configuration `Validate()` refuses.
2. It removes whatever an earlier launcher process left behind: containers and volumes labelled
   `trax.sandbox.launcher` on podman, its Jobs and run directories on a cluster. A run directory
   it cannot remove is logged and left, rather than stopping the start.
3. It runs the canary: one sandbox under the configured runtime, with `canary.json` in place of a
   request. The sandbox reports its kernel and tries to connect to the metadata endpoints
   (169.254.169.254, 169.254.170.23, 169.254.170.2, fd00:ec2::254), addresses off the host, the
   cluster's API server and the launcher's own address on a cluster, and `CanaryProbes`. The launcher
   does not start when the sandbox reports this node's kernel (the runtime is not a VM, whatever its
   name), reaches any probe, has an interface other than loopback on podman, or does not run at all
   (the runtime is missing).

Per request, on `POST /trax/sandbox/run` (`GET /healthz` answers `ok`):

1. A missing, malformed, stale (over 60 seconds either way) or wrongly signed request, or one whose
   nonce was already accepted, gets a bare 401 and nothing else. A body over the input cap gets 413
   unread.
2. A request that is not the fixed shape, names an unknown profile, or asks for more than a cap is
   refused: 400, a signed `Refused` answer with the reason.
3. Past `MaxConcurrent`: 429, a signed `Busy` answer.
4. Otherwise it runs the sandbox and answers 200 with how it ended: `Completed` with the result and
   files, or `TimedOut`, `OutOfMemory`, `Exited`, `OutputRefused` or `Failed` with the reason. A
   killed run hands nothing back.

The output is checked entry by entry before anything is handed on: regular files and directories
only, every path inside the output, within the entry and byte caps. Only files under `files/` go
back, in a tar the launcher writes itself.

## One host

Podman cannot run Kata: Kata Containers 4 has no OCI command line for it to call. With `Kata`
configured the podman backend's canary fails and says so. On podman the isolating runtime is
gVisor; one machine with Kata is k3s and the Kubernetes backend below.

The podman backend talks to rootless podman through its API socket. Per run it makes an input and
an output volume, both tmpfs with a size (the output's is the declared disk), and a holder: a
container of `HolderImage` under podman's default runtime that keeps both mounted, since a tmpfs
volume's contents go when the last container using it stops. The sandbox itself runs the profile's
image under the configured runtime with no network namespace but loopback, a read-only root, every
capability dropped, no new privileges, the declared memory (and no swap beyond it), CPU and the pids
cap, a tmpfs `/tmp` of the declared disk, the input read-only and the output `noexec`. The input
goes in, and the output comes out, through the holder; the sandbox is never exec'd into. Every
container and volume of the run is removed afterwards, whatever happened.

Never mount the podman socket into the hub or a sandbox: it is a way out of any container holding
it. Run the launcher as a user of its own, whose rootless podman nothing else uses.

## A cluster

The Kubernetes backend creates one Job per run in the dedicated namespace and pins it to the
launcher's node. The pod names the RuntimeClass, uses a ServiceAccount with no token, no service
links, no host namespaces and no DNS resolver, runs as the profile's non-root user with every
capability dropped and a read-only root, and mounts only its own run's `in` (read-only) and `out` on
the runs claim, by subPath, and an in-memory emptyDir as `/tmp`. The Job dies at the timeout and is
never retried. The launcher reads the output directory after the pod is gone, without following a
link or opening anything but a regular file, and checks its size every `PollInterval` while the
sandbox runs. A walk does not see a file the sandbox deleted while still holding it open, so the
launcher also reads the runs volume's free space from its filesystem: below `RunsMinFreePercent` no
run starts, and every running one is killed and fails, its log saying why. Neither check is a
per-run limit. The claim's size is a ceiling only where its storage class enforces capacity; k3s's
default `local-path` does not, so there the floor is what keeps the node's disk from filling.

`KubernetesSandboxManifests.Render` writes everything the namespace needs, from the same options,
as one multi-document YAML, and so does the launcher itself when run with
`--trax-sandbox-manifests <file> --launcher-image <ref@sha256:...> --hub-namespace <ns>`
(`--runs-storage` sets the claim's size): it reads its configuration as it would to run, needs no
key, and starts nothing. The objects are, admission policies first so every later pod is admitted
under them: the namespace at Pod Security `restricted`; the launcher's ServiceAccount
and a Role on Jobs (create, get, list, watch, delete) and pods (get, list, watch), with no
subresource, so no `pods/exec` or `pods/log`; the sandbox ServiceAccount, with no token and bound to
nothing; a deny-all NetworkPolicy for sandbox pods and one admitting only the hub's namespace to the
launcher; the runs claim; the launcher's Deployment (one replica, its key from the Secret named
`KeySecretName`, every cap, canary probe and runtime setting it was rendered from, and a writable
`/tmp` on its read-only root) and Service (`LauncherName`); and two ValidatingAdmissionPolicies with their
bindings. The policies refuse any Job, and any pod other than the launcher's own, that does not
name the RuntimeClass, run one container from the profiles' digests, use the sandbox ServiceAccount
with no token, carry only `HOME` and `TMPDIR`, mount only the runs claim and an emptyDir, and stay
off the host's namespaces; the launcher's pod must run the launcher image. The ServiceAccounts in the
namespace must have no cloud identity: on EKS, create no Pod Identity association or IRSA annotation
for them. The environment and volume rules refuse a pod a credential webhook has added either to.

## Example

```csharp compile
using Trax.Scheduler.Sandbox.Launcher;

await TraxSandboxLauncher.RunAsync(args);
```

```json
{
  "Trax": {
    "SandboxLauncher": {
      "Runtime": "Kata",
      "Backend": "Podman",
      "SigningKey": "<32 random bytes, base64>",
      "ListenSocketPath": "/run/trax-sandbox/launcher.sock",
      "Profiles": {
        "builder": { "Image": "registry.example.com/builder@sha256:<digest>" }
      },
      "Caps": { "MaxConcurrent": 2, "MaxMemoryMiB": 2048 },
      "Podman": {
        "SocketPath": "/run/user/1001/podman/podman.sock",
        "HolderImage": "registry.k8s.io/pause@sha256:<digest>"
      }
    }
  }
}
```

## See also

- [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox)
- [AddTraxSandbox](/docs/sdk-reference/scheduler-api/add-trax-sandbox)
- [Sandbox threat model](/docs/security/sandbox-threat-model)
