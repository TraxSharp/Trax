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

**On a cluster** each run gets a directory on a volume the launcher's pod mounts; the sandbox mounts
that run's `in` read-only and its `out` writable, by subPath, and is pinned to the launcher's node.
The launcher reads the directory after the Job's pod is gone, walking it without following a link
and letting the tar writer read each entry's type with lstat, so a link or a named pipe is recorded
as one and never opened. The output directory has no size of its own there, so the launcher walks
it while the sandbox runs and kills a run that goes over. A walk misses a file the sandbox deleted
while still holding it open, so the launcher also reads the volume's free space from the filesystem,
whose count includes such a file: below `RunsMinFreePercent` no run starts and every running one is
killed. That floor is shared by every run on the volume, not a limit per run. The claim's size is a
ceiling only where its storage class enforces capacity, and k3s's default `local-path` does not, so
on k3s the volume is the node's disk and the floor is what keeps it from filling.

A per-run limit the kernel enforces, as the tmpfs is on one host, is not done on a cluster yet. A
memory-backed emptyDir with a size would be one, but it is gone with the pod, before the launcher
can read it, and getting it out needs something trusted in the pod that outlives the sandbox. A
per-run volume needs a storage class that enforces capacity, which k3s does not ship.

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

- `KubernetesSandboxBackendTests` also pins the watch loop: the disk kill, the entry kill, the
  start timeout, the free-space floor before a start and while a run goes on, and the cleanup after
  a caller gives up.

On k3s with Kata the subPath mounts, the size polling (a run filling its output was killed) and the
entry polling (a run leaving too many files was killed and refused) worked. Not covered: neither
check is a hard cap per run. Between two polls a sandbox can write as fast as the node's disk takes
it, and a deleted file it holds open is seen only by the shared free-space floor.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-24**: Too many files is its own refusal on a cluster, not a disk overrun.
- **2026-11-25**: The claim's size is not a ceiling on `local-path`; a free-space floor on the runs
  volume stands in until a cluster has a per-run limit.
