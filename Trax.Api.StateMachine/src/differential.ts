import type { SnapshotMachine } from "./machine";
import type { Snapshot } from "./types";

/**
 * The exhaustive cross-runtime differential corpus. TypeScript is the oracle: it drives the engine over
 * every reachable snapshot x trigger x sample-input and records the outcome as canonical wire (on a
 * transition) or a rejection code (PD7: code only, never detail). The corpus is committed; every runtime
 * replays it and must reproduce each outcome, which proves the hand-written twin reducers stay identical
 * (PD1). C# replays the same file (see DifferentialConformanceTests).
 *
 * Coverage:
 *   1. BFS from the initial snapshot over the machine's own transitions — reaches every state whose
 *      context a trigger can populate.
 *   2. Declared `seeds` (the IR's `differential.seeds`) — representative valid contexts for states a
 *      trigger cannot reach (e.g. checkout's Review/Paid, whose items/total arrive via autosave).
 * At every reachable snapshot we fire every trigger x sample-input; the bulk of the corpus is rejections.
 */
export interface DifferentialSpec {
  id: string;
  version: number;
  states: string[];
  triggers: string[];
  /** The IR's outcome triggers (`Fetching.done`), fired like triggers; their samples are train outputs. */
  outcomes?: Record<string, unknown>;
  differential?: {
    samples?: Record<string, unknown[]>;
    seeds?: Record<string, Record<string, unknown>>;
    /**
     * A dense validator/guard probe: each context is crossed with EVERY state, producing given snapshots
     * that may be unreachable on purpose (a machine never lands in state X with context C, but a client or
     * a bug might send it). This is what complex machines need beyond BFS + seeds — it is where the
     * invalid-context and guard-failed divergences hide. Keep the set small and shape-valid.
     */
    contexts?: Record<string, unknown>[];
  };
}

export type DifferentialCase = {
  given: Snapshot;
  when: { trigger: string; input?: unknown };
  expect:
    | { outcome: "transitioned"; wire: string }
    | { outcome: "rejected"; reason: string };
};

export interface DifferentialCorpus {
  machine: string;
  version: number;
  generated: string;
  cases: DifferentialCase[];
}

const GENERATED =
  "GENERATED differential corpus. Do not hand-edit; regenerate with UPDATE_DIFFERENTIAL=1. " +
  "TypeScript is the oracle; every runtime replays and must reproduce each outcome.";

export function enumerate<S extends string, T extends string>(
  machine: SnapshotMachine<S, T>,
  spec: DifferentialSpec,
): DifferentialCorpus {
  // Outcome triggers are events the twin applies, so they are enumerated after the user's own triggers; the
  // corpus covers only the pure half (the twin holds no token, so it cannot tell a stale outcome from a live one).
  const triggers = [
    ...spec.triggers,
    ...Object.keys(spec.outcomes ?? {}).sort(),
  ];
  const samples = spec.differential?.samples ?? {};
  const seeds = spec.differential?.seeds ?? {};
  const contexts = spec.differential?.contexts ?? [];

  // Every trigger is fired with no payload plus each declared sample input.
  const inputsFor = (trigger: string): unknown[] => [
    undefined,
    ...(samples[trigger] ?? []),
  ];

  // Phase 1 — discover every reachable snapshot by driving the machine's own transitions from the
  // initial snapshot and each declared seed.
  const reachable = new Map<string, Snapshot>();
  const queue: Snapshot[] = [machine.createInitialSnapshot()];
  for (const [state, context] of Object.entries(seeds)) {
    queue.push({ machine: spec.id, version: spec.version, state, context });
  }
  while (queue.length > 0) {
    const snap = queue.shift() as Snapshot;
    const wire = machine.serialize(snap);
    if (reachable.has(wire)) continue;
    reachable.set(wire, snap);
    for (const trigger of triggers) {
      for (const input of inputsFor(trigger)) {
        const r = machine.advance(snap, trigger, input);
        if (r.outcome === "transitioned") queue.push(r.snapshot);
      }
    }
  }

  // Dense probe — cross each declared context with EVERY state. These given snapshots may be unreachable
  // on purpose; they exercise guards and target validators that BFS never lands on (this is where the
  // invalid-context and guard-failed divergences a complex machine can hide are caught).
  for (const context of contexts) {
    for (const state of spec.states) {
      const snap = {
        machine: spec.id,
        version: spec.version,
        state,
        context,
      } as Snapshot;
      reachable.set(machine.serialize(snap), snap);
    }
  }

  // Phase 2 — at every given snapshot (ordinal-sorted for a stable golden) fire every trigger x input.
  const cases: DifferentialCase[] = [];
  for (const wire of [...reachable.keys()].sort()) {
    const given = JSON.parse(wire) as Snapshot;
    const snap = reachable.get(wire) as Snapshot;
    for (const trigger of triggers) {
      for (const input of inputsFor(trigger)) {
        const r = machine.advance(snap, trigger, input);
        const when = input === undefined ? { trigger } : { trigger, input };
        cases.push(
          r.outcome === "transitioned"
            ? {
                given,
                when,
                expect: {
                  outcome: "transitioned",
                  wire: machine.serialize(r.snapshot),
                },
              }
            : {
                given,
                when,
                expect: { outcome: "rejected", reason: r.reason },
              },
        );
      }
    }
  }

  return {
    machine: spec.id,
    version: spec.version,
    generated: GENERATED,
    cases,
  };
}

/** Deterministic golden serialization (stable key order + trailing newline) for a clean git diff. */
export function serializeCorpus(corpus: DifferentialCorpus): string {
  return `${JSON.stringify(corpus, null, 2)}\n`;
}
