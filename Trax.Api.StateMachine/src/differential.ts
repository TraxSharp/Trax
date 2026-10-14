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
 *      trigger cannot reach (e.g. checkout's Review/Paid, whose items/total arrive via autosave). A state may
 *      have several seeds, and each is a start point the search expands like the initial snapshot.
 * At every reachable snapshot we fire every trigger x sample-input; the bulk of the corpus is rejections.
 */
export interface DifferentialSpec {
  id: string;
  version: number;
  states: string[];
  triggers: string[];
  /** The IR's outcome triggers (`Fetching.done`), fired like triggers; their samples are train outputs. */
  outcomes?: Record<string, unknown>;
  /** The IR's timers (`Review.after`), fired through `applyTimer` with no input. */
  timers?: ReadonlyArray<{ trigger: string }>;
  differential?: {
    samples?: Record<string, unknown[]>;
    /** A state's seed context, or a list of them when the state is seeded more than once. */
    seeds?: Record<string, Record<string, unknown> | Record<string, unknown>[]>;
    /**
     * A dense validator/guard probe: each context is crossed with EVERY state, producing given snapshots
     * that may be unreachable on purpose (a machine never lands in state X with context C, but a client or
     * a bug might send it). This is what complex machines need beyond BFS + seeds — it is where the
     * invalid-context and guard-failed divergences hide. Keep the set small and shape-valid.
     */
    contexts?: Record<string, unknown>[];
  };
}

// Every seed context of a state: the IR writes a single seed as the context itself, several as a list.
function seedContexts(
  seeds: Record<string, unknown> | Record<string, unknown>[],
): Record<string, unknown>[] {
  return Array.isArray(seeds) ? seeds : [seeds];
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
  // Outcome triggers are enumerated after the user's own triggers, and each is applied through the entry point
  // that owns it, `applyOutcome`, as the C# replay applies it through its outcome path; `advance` refuses them.
  // The corpus covers only the pure half (the twin holds no token, so it cannot tell a stale outcome from a live
  // one).
  //
  // Timer triggers come last, each through `applyTimer`, as the C# replay applies them through `AdvanceTimer`. A timer
  // carries no input, and the corpus records only the declared timer and its transition: never a due time or a
  // revision, which the server keeps beside the snapshot.
  const outcomes = Object.keys(spec.outcomes ?? {}).sort();
  const outcomeSet = new Set(outcomes);
  const timers = (spec.timers ?? []).map((t) => t.trigger).sort();
  const timerSet = new Set(timers);
  const triggers = [...spec.triggers, ...outcomes, ...timers];
  const fire = (snap: Snapshot, trigger: string, input: unknown) =>
    outcomeSet.has(trigger)
      ? machine.applyOutcome(snap, trigger, input)
      : timerSet.has(trigger)
        ? machine.applyTimer(snap, trigger)
        : machine.advance(snap, trigger, input);
  const samples = spec.differential?.samples ?? {};
  const seeds = spec.differential?.seeds ?? {};
  const contexts = spec.differential?.contexts ?? [];

  // Every trigger is fired with no payload plus each declared sample input.
  const inputsFor = (trigger: string): unknown[] =>
    timerSet.has(trigger) ? [undefined] : [undefined, ...(samples[trigger] ?? [])];

  // Phase 1 — discover every reachable snapshot by driving the machine's own transitions from the
  // initial snapshot and each declared seed.
  const reachable = new Map<string, Snapshot>();
  const queue: Snapshot[] = [machine.createInitialSnapshot()];
  for (const [state, contexts] of Object.entries(seeds)) {
    for (const context of seedContexts(contexts)) {
      queue.push({ machine: spec.id, version: spec.version, state, context });
    }
  }
  while (queue.length > 0) {
    const snap = queue.shift() as Snapshot;
    const wire = machine.serialize(snap);
    if (reachable.has(wire)) continue;
    reachable.set(wire, snap);
    for (const trigger of triggers) {
      for (const input of inputsFor(trigger)) {
        const r = fire(snap, trigger, input);
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
        const r = fire(snap, trigger, input);
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
