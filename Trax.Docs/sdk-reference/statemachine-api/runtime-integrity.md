---
layout: default
title: Runtime integrity
parent: State Machine API
grand_parent: SDK Reference
nav_order: 10
---

# Runtime integrity checks

The [differential](/docs/statemachine#two-runtimes-one-behavior) and drift tests prove the C# machine and the
generated TypeScript twin agree at **build time**, from one commit. At **runtime** the two engines run in
different processes at possibly different versions: the twin is baked into whatever client bundle the user
loaded (which may be days old), the C# machine is the deployed server. These three checks catch the divergence
that build-time tests structurally cannot: client/server version skew in production.

All three are opt-in from the client side: a client that sends nothing is unaffected, so they roll out
gradually as clients start sending the new fields.

## Schema-hash handshake

Every machine exposes a stable content hash of its behaviour:

| Member | Where | Value |
| --- | --- | --- |
| `IMachine.SchemaHash` | C# server | lowercase-hex SHA-256 of `ExportIr()`; `null` for a raw-delegate machine (no exportable IR, so no twin and no handshake) |
| `TypedMachine.schemaHash` / the twin's `irHash` | TypeScript client | SHA-256 of the same committed IR, embedded in the generated twin |

Both hash the committed IR, so they are equal by construction (the drift tests pin `ExportIr()` to the
committed `ir.json`, and the twin hashes that same file with its trailing newline stripped to match `ExportIr()`).

The client sends its `schemaHash` on each snapshot mutation (`saveSnapshot`, `advanceSnapshot`, `loadSnapshot`,
`sendSnapshot`). When it differs from the server's registered machine, the request is refused with a
[`schema-mismatch`](/docs/sdk-reference/statemachine-api/result-codes) problem, so a stale client reloads
instead of writing under an outdated contract. A client that sends no hash is not checked.

```csharp
// The registered machine's hash, for the handshake.
string? hash = registry.SchemaHash("checkout");
```

A machine with no exportable IR returns `null`, and the guard treats null as "no check" rather than throwing.

## Divergence detection

The schema hash catches a version mismatch; this catches a genuine behavioural disagreement on a *real* input.
On `advanceSnapshot`, the client may send `clientResult`, the snapshot its twin computed for the advance, as
canonical wire. The server re-drives the advance authoritatively, then compares:

```
client twin: (pre-state, trigger, input) -> clientResult
server C#:   (pre-state, trigger, input) -> serverResult   <- authoritative
```

If the two canonical wires differ, the advance is refused with a
[`client-divergence`](/docs/sdk-reference/statemachine-api/result-codes) problem and the client reloads. The
server's result is always authoritative; a client that sends no `clientResult` is not checked.

Because the server drives from the stored snapshot and, under optimistic concurrency, the client's pre-state
equals the last server snapshot, a post-state mismatch is a real divergence signal: a skew the schema hash
missed, or a bug.

## Startup self-check

The same committed [differential corpus](/docs/statemachine#two-runtimes-one-behavior) the CI test replays can
be replayed by the running server at startup, proving the deployed C# engine still reproduces the machine's
behaviour.

| Member | Returns | Meaning |
| --- | --- | --- |
| `IMachine.Corpus` | `string?` | the machine's committed golden corpus, or null if it ships none |
| `IMachine.SelfCheck()` | `IReadOnlyList<string>` | replays `Corpus` through the machine's own engine; one diff per case it fails to reproduce (empty == agreement, and empty when there is no corpus) |
| `SnapshotSelfCheck.Run(machines)` | `IReadOnlyList<string>` | runs every machine's self-check and aggregates the diffs, machine-prefixed |

`SnapshotSelfCheck.Run` is dependency-free (the engine does not pull in health-check or hosting abstractions);
a host injects the discovered machines and wires the result into a health check or an `IHostedService`:

```csharp
services.AddHealthChecks().AddCheck("state-machines", () =>
{
    var diffs = SnapshotSelfCheck.Run(machines);
    return diffs.Count == 0
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy(string.Join("\n", diffs));
});
```

A machine ships its corpus by overriding `Corpus` (e.g. from an embedded resource); a machine that ships none
is skipped, not failed.

## Result codes

| Code | Returned by | Meaning |
| --- | --- | --- |
| `schema-mismatch` | save, advance, load, send | the client's `schemaHash` differs from the server's machine; reload |
| `client-divergence` | advance | the client's `clientResult` differs from the server's authoritative result; reload |

Both surface on the mutation's `problem` field like every other [result code](/docs/sdk-reference/statemachine-api/result-codes).
