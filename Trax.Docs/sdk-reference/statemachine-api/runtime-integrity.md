---
title: Runtime integrity
description: "Reference for the runtime checks that catch drift between a C# machine and its TypeScript twin: schema-hash handshake, divergence detection and self-check."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 10
package: Trax.Effect.StateMachine.Persistence
namespace: Trax.Effect.StateMachine.Persistence
source:
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotSelfCheck.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/SnapshotMachineRegistry.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/Machine.cs
---

# Runtime integrity

Three checks that compare the C# machine a server runs with the TypeScript twin a client loaded, which may come from
an older bundle: the schema-hash handshake, divergence detection on an advance, and the self-check that replays the
committed corpus through the deployed engine. The [differential tests](/docs/statemachine/codegen-pipeline#the-differential-corpus)
prove the two agree at build time, from one commit; these run in production. All three are opt in from the client
side: a client that sends nothing is not checked.

## Members

| Member | Where | Value |
| --- | --- | --- |
| `IMachine.SchemaHash` | C# server | lowercase-hex SHA-256 of `ExportIr()`; `null` for a machine whose IR export is refused by design, raw-delegate or declarative with a delegate guard, reducer or validator left in (no exportable IR, so no twin and no handshake) |
| `TypedMachine.schemaHash` / the twin's `irHash` | TypeScript client | SHA-256 of the same committed IR, embedded in the generated twin |

| Member | Returns | Meaning |
| --- | --- | --- |
| `IMachine.Corpus` | `string?` | the machine's committed golden corpus, or null if it ships none |
| `IMachine.SelfCheck()` | `IReadOnlyList<string>` | replays `Corpus` through the machine's own engine; one diff per case it fails to reproduce (empty == agreement, and empty when there is no corpus) |
| `SnapshotSelfCheck.Run(machines)` | `IReadOnlyList<string>` | runs every machine's self-check and aggregates the diffs, machine-prefixed |
| `IHealthChecksBuilder.AddTraxStateMachineSelfCheck(string name = "state-machines")` | `IHealthChecksBuilder` | registers `SnapshotSelfCheck.Run` over every discovered machine as an ASP.NET Core health check |

## Behavior

### Schema-hash handshake

`IMachine.SchemaHash` and the twin's hash are taken over the same committed IR, so they are equal by construction (the drift tests pin `ExportIr()` to the
committed `ir.json`, and the twin hashes that same file with its trailing newline stripped to match `ExportIr()`).

The client sends its `schemaHash` on each snapshot mutation (`saveSnapshot`, `advanceSnapshot`, `loadSnapshot`,
`sendSnapshot`). When it differs from the server's registered machine, the request is refused with a
[`schema-mismatch`](/docs/sdk-reference/statemachine-api/result-codes) problem, so a stale client reloads
instead of writing under an outdated contract. A client that sends no hash is not checked. That optional
handshake belongs to the generic mutations only. A machine served through its own typed operations takes no
`schemaHash`: its [persisted documents](/docs/sdk-reference/graphql-api/machines#persisted-documents) carry the IR
hash in their ids, and a node answers another version's id `client-outdated` or `server-outdated`, so the check
cannot be skipped by leaving a field out.

`ISnapshotMachineRegistry.SchemaHash(machine)` returns `null` for a machine with no exportable IR, and the check
treats null as no check rather than throwing.
The hash is computed once, on first read, and every concurrent reader waits for that value: a request that
arrives while the first one is still building the machine gets the hash, never a `null` that would switch the
check off.

### Divergence detection

The schema hash catches a version mismatch; this catches a genuine behavioural disagreement on a *real* input.
On `advanceSnapshot`, the client may send `clientResult`, the snapshot its twin computed for the advance, as
canonical wire. The server re-drives the advance authoritatively and compares before it writes anything:

```
client twin: (pre-state, trigger, input) -> clientResult
server C#:   (pre-state, trigger, input) -> serverResult   <- authoritative
```

If the two canonical wires differ, the advance is refused with a
[`client-divergence`](/docs/sdk-reference/statemachine-api/result-codes) problem and the client reloads. The
refusal is real: the stored draft is exactly as it was before the request, so the reload shows the pre-state
and a retry fires the trigger once. A client that sends no `clientResult` is not checked.

The comparison lives in `ISnapshotDraftService.Advance(userKey, id, trigger, input, requestId, clientResult)`,
which computes the advance, checks it, and only then writes. A custom `ISnapshotDraftService` that does not
override that overload throws `NotSupportedException` when handed a client result, rather than persisting an
advance it would then report as refused.

### Startup self-check

The running server replays the same committed [differential corpus](/docs/statemachine/codegen-pipeline#the-differential-corpus)
the CI test replays, through the deployed C# engine.

The check is Healthy when every machine reproduces its corpus, and Unhealthy with every diff in its
description otherwise. It replays each corpus in full on every call and does not observe the health-check
cancellation token. Nothing runs it until it is registered, and registering it does not gate startup; a host that
should refuse to start on a drifted engine calls `SnapshotSelfCheck.Run(machines)` from its own startup code.

A machine ships its corpus by overriding `Corpus` (for example from an embedded resource); a machine that ships none
is skipped, not failed.

Both refusals surface on the mutation's `problem` field: `schema-mismatch` (save, advance, load, send) and
`client-divergence` (advance; nothing was written). See [Result codes](/docs/sdk-reference/statemachine-api/result-codes).

## Examples

```csharp compile
var registry = serviceProvider.GetRequiredService<ISnapshotMachineRegistry>();
string? hash = registry.SchemaHash("checkout");
```

```csharp compile
builder.Services.AddHealthChecks().AddTraxStateMachineSelfCheck();
```

## See also

- [AddTraxStateMachineSelfCheck](/docs/sdk-reference/statemachine-api/add-trax-state-machine-self-check).
- [MachineConformance](/docs/sdk-reference/statemachine-api/machine-conformance), the build-time checks.
- [Result codes](/docs/sdk-reference/statemachine-api/result-codes).
