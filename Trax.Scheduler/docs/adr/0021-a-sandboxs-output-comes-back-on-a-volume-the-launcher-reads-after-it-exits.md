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
as one and never opened. The output directory has no size of its own there, so the launcher checks
its size while the sandbox runs and kills a run that goes over; the claim's size is the hard
ceiling behind that.

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

On k3s with Kata the subPath mounts, the size polling (a run filling its output was killed) and the
entry polling (a run leaving too many files was killed and refused) worked. Not covered: polling is
not a hard cap. Between two polls a sandbox can write as fast as the node's disk takes it; the runs
claim's size is the hard ceiling.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-24**: Too many files is its own refusal on a cluster, not a disk overrun.
