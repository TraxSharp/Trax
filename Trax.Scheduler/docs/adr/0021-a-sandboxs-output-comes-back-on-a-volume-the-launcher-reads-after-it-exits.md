---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A sandbox's output comes back on a volume the launcher reads after it exits

The sandbox has no network path at all, so its result and files cannot be sent anywhere. It writes
them to an output volume, and the launcher reads that volume once the sandbox has exited or been
killed, checks it entry by entry, writes a fresh tar from the entries it accepted, hands that to the
hub, and deletes the volume. Nothing from the sandbox is ever extracted to disk, and the hub reads
the launcher's tar again with the same checks.

## Status

**Accepted.**

## Why this is written down

Because "results go through the launcher" and "the sandbox cannot reach the launcher" contradict
each other unless the path is a volume. The alternatives weighed:

- **The sandbox posts its result to the launcher.** Rejected: it needs a network path, and the
  launcher's port is then reachable from untrusted code.
- **Signed object-storage URLs.** Rejected: a storage dependency, and still a network path.
- **A credential scoped to the run's own database row.** Rejected: a database credential in the
  sandbox.

What the launcher reads is entirely the sandbox's to write, so the checks are the boundary: regular
files and directories only (a symbolic link, a hard link, a device or a named pipe refuses the whole
output), every path relative and inside the output with no `..`, `.`, empty segment, backslash or
control character, at most a set number of entries and of bytes, and a cap on the raw tar so a huge
header costs nothing. A refusal names the entry with control characters dropped, since it ends up
in a log and a run's failure reason.

**On one host** the volumes are tmpfs with a size, so the output is capped by the kernel. A tmpfs
volume is unmounted, and its contents lost, when the last container using it stops, so each run has
a holder: a launcher-owned container of a pinned pause image that keeps both volumes mounted and
never runs anything of the sandbox's. The input goes in through the holder before the sandbox
starts; the output comes out through it, as podman's tar of the directory, after the sandbox exits.

**On a cluster** each run's `/sandbox/out` is a volume of its own: a generic ephemeral volume of
its declared disk on `OutputStorageClassName`, made with the sandbox's pod and deleted with it, so
the size is a per-run limit the storage enforces, as the tmpfs is on one host. That needs a class
that enforces capacity (TopoLVM, Longhorn, a cloud block-storage driver), which k3s does not ship.
The launcher refuses to start without one: `Validate()` refuses no class and `local-path`, and at
startup it reads the class (a ClusterRole lets it `get` that one class) and refuses a host-path
provisioner. Whether any other class enforces capacity nothing in the API says, so the deploy's
choice of class is the claim, and the docs name the classes that qualify.

The volume lasts only as long as the pod, so it is read after the sandbox exits and before its pod
is deleted. The sandbox's pod is then in a terminal phase with `restartPolicy: Never` and no
retries, so nothing writes the volume again. The launcher cannot mount a volume into its own
running pod, so a collector does the reading: a Job of the launcher's own image, under the same
RuntimeClass and hardening, as the profile's user, with the run's output claim read-only and only
that run's `collected` directory on the shared runs claim writable. It walks the output without
following a link, lets the tar writer read each entry's type with lstat (a link or a named pipe is
recorded as one and never opened), applies the caps, and writes a tar of what it accepted or the
reason it refused. The launcher reads that tar again with the same checks. So the untrusted files
are opened only in the collector's pod, and no untrusted code runs in it or the launcher's.

The shared runs claim now holds only each run's input and collected tar, both bounded by the caps.
It keeps the free-space floor: below `RunsMinFreePercent` free no run starts and every running one
is killed.

Rejected for the per-run limit: a memory-backed emptyDir with a size, read back by a collector in
the same pod, which would share the Kata VM with the untrusted code; and keeping the shared floor
alone, which one hostile run can drive down to fail every other run.

## Exemplars

- `SandboxArchiveTests` pins every refusal: links, devices, pipes, paths out of the output,
  duplicates, the entry, byte and raw caps.
- `PodmanSandboxBackendTests` pins the holder, the order of calls, the locked-down spec, and that
  every container and volume of a run is removed whatever happened.
- `DirectoryOutputTests` pins that a link is not followed or descended and a named pipe is not
  opened.
- `SandboxRunnerTests` pins that a killed run hands nothing back and that only files under
  `files/` return.
- [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox) is the rule this produces.

- `KubernetesSandboxBackendTests` pins the cluster path against an API that refuses every
  subresource, as the launcher's Role does.

- `KubernetesSandboxBackendTests` also pins the watch loop (the start timeout, the free-space floor
  before a start and while a run goes on, the cleanup after a caller gives up), that the collector
  reads the exited sandbox's own claim read-only before that pod is deleted, that a killed run is
  never collected, and that the start is refused without a class that can enforce capacity.
- `KubernetesJobSpecTests` pins the output volume's shape and the collector's, and
  `KubernetesSandboxManifestsTests` the admission rules that allow exactly that shape.
- `DirectoryOutputTests` also pins the collector's tar and refusal and the launcher's read of them.

On k3s with Kata the subPath input mount worked. Not covered: the per-run output volume and the
collector have not been run on a cluster; `KubernetesEscapeTests.cs` needs a cluster with a class that
enforces capacity and runs by hand. The cap is only as hard as the class makes it.

## Changelog

- **2026-11-25**: A cluster run's output is a generic ephemeral volume of its declared disk on a
  class that enforces capacity, read by a collector Job before the sandbox's pod is deleted; the
  launcher refuses to start without such a class. The output size and entry polling are gone.
- **2026-11-25**: The claim's size is not a ceiling on `local-path`; a free-space floor on the runs
  volume stands in until a cluster has a per-run limit.
- **2026-11-24**: Too many files is its own refusal on a cluster, not a disk overrun.
- **2026-11-24**: Recorded.
