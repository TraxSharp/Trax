---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A sandbox is started only by the launcher, from a fixed request

A train marked `[TraxSandbox]` runs in a fresh sandbox per run, and only a separate process, the
sandbox launcher (`Trax.Scheduler.Sandbox.Launcher`), can start one. On one host it is the only
process holding the container runtime's socket; on a cluster its ServiceAccount may create, read and
delete Jobs in one dedicated namespace and read pods there, with no `pods/exec` or `pods/log`. The
hub asks it over a channel signed both ways, and the request has a fixed shape: run id, profile,
train name, input and the declared limits. Everything that shapes a sandbox (image, command,
environment, mounts, ServiceAccount, runtime, network, privileges, capabilities) is the launcher's
deploy-time configuration, so a compromised hub can ask for sandboxes but cannot shape them.

## Status

**Accepted.**

## Why this is written down

Because the obvious designs hand the hub, or the untrusted code, a way out.

- **The hub creates the Jobs or containers itself.** Rejected: the hub then holds the runtime
  socket or a Role that can create pods, and anything that compromises it (an injected input, a
  dependency) can start a privileged pod, mount the host, or name a ServiceAccount with cloud
  credentials.
- **A Trax worker inside each sandbox claims its run from the database.** Rejected: a database
  credential in the same process as untrusted code.
- **A launcher with an open request shape** (the hub passes an image or a command and the launcher
  applies a policy to it). Rejected: every field is one more thing the policy must get right. The
  request has none of them, and the wire's JSON options refuse any member they do not know, so a
  hub that adds `"image"` or `"privileged"` is refused for it rather than having it ignored.

On a cluster the Role alone is not enough: it still lets the launcher's ServiceAccount create a
runc pod, a privileged one, one with any image, or one that names another ServiceAccount, including
one with a Pod Identity association. So the namespace is at Pod Security `restricted`, sandbox pods
use a ServiceAccount with no token and no binding, a deny-all NetworkPolicy covers them, and two
ValidatingAdmissionPolicies refuse any Job or pod in the namespace that does not name the configured
RuntimeClass, run one container from the deploy's digest allowlist, carry only `HOME` and
`TMPDIR`, and mount only the runs claim and an emptyDir. The admission rules are rendered from the
same options the launcher runs with (`KubernetesSandboxManifests`), so the two cannot disagree.

The signature is the runner signature's shape (HMAC-SHA256 over a timestamp, a nonce and the body),
with its own prefix so neither can be presented as the other. Every answer from the point the MAC
verifies, a refusal included, is signed and bound to the request's nonce, so a hub reads nothing the
launcher did not write for that request. Accepted nonces are kept in memory, which is why the
launcher runs as one replica.

The concurrency cap refuses rather than queues: a hub cannot pile work up on the host, and a busy
answer comes back at once with the reason.

## Exemplars

- `SandboxRequestReaderTests` pins that each field a hub could use to shape a sandbox is refused,
  and that limits over the caps are refused, not lowered.
- `LauncherEndpointTests` pins the hostile-hub cases against the real route: unsigned, wrong key,
  stale and replayed requests get a bare 401; shaping fields and over-cap limits get a signed
  refusal; past the concurrency cap is busy; in none does the backend start anything.
- `SandboxChannelSignatureTests` pins the MAC, its binding of an answer to its request, and that a
  runner signature does not verify.
- `TraxSandboxClientTests` pins that the hub sends only the fixed shape and reads only a signed
  answer for its request.
- `TraxSandboxEntryPointTests` pins that a sandbox runs only a `[TraxSandbox]` train, by its exact
  name.
- `KubernetesJobSpecTests` and `KubernetesSandboxManifestsTests` pin the Job, the Role (no
  subresources), the tokenless ServiceAccount, the deny-all policy and the admission rules.
- [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox) and the
  [sandbox threat model](/docs/security/sandbox-threat-model) are the rule this produces.

The admission policies were applied to k3s 1.36 and refused, by server-side dry run, a pod with no
RuntimeClass or another one, a foreign digest, the right image by tag, extra env, env or envFrom
from a Secret, a Secret or projected token volume, a token mounted or left to the default, the
launcher's account, two containers, another claim, a launcher-labelled pod with a foreign image, and
a Job whose pods carry the launcher's label; they admitted the sandbox's own pod and Job shape.

Not covered: no test runs the policies in CI; their CEL is checked there only as text.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-24**: Applied to k3s; the launcher reads Jobs with a plain get, since its Role grants
  no `jobs/status`.
