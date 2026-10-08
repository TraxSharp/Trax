import {
  RehydrationErrorCodes,
  RejectionReasons,
  type AdvanceResult,
  type RehydrationResult,
} from "./results";
import type {
  MachineDefinition,
  Snapshot,
  TransitionDefinition,
} from "./types";

/**
 * Interprets a {@link MachineDefinition} over {@link Snapshot}s — the TypeScript half of the
 * cross-language contract. The C# {@link SnapshotMachine} (Trax.Effect.StateMachine) is the other
 * half, and the shared conformance fixtures prove they behave identically.
 *
 * A plain reducer: `advance` matches the edges out of the snapshot's state, picks the FIRST whose
 * guard passes, and takes that edge's `to` as the destination — so the destination and the reducer
 * come from the SAME chosen edge and cannot disagree. Every public operation is total (never throws).
 */
export class SnapshotMachine<S extends string, T extends string> {
  private readonly stateSet: ReadonlySet<string>;
  private readonly outcomeTriggers: ReadonlySet<string>;

  constructor(private readonly def: MachineDefinition<S, T>) {
    this.stateSet = new Set<string>(def.states);
    this.outcomeTriggers = new Set<string>(def.outcomeTriggers ?? []);
  }

  get definition(): MachineDefinition<S, T> {
    return this.def;
  }

  createInitialSnapshot(): Snapshot {
    return {
      machine: this.def.id,
      version: this.def.version,
      state: this.def.initialState,
      context: this.def.createInitialContext(),
    };
  }

  advance(snapshot: Snapshot, trigger: string, input?: unknown): AdvanceResult {
    // Unknown state/trigger tokens can fire nothing, so they degrade to a handled rejection.
    if (!this.stateSet.has(snapshot.state))
      return rejected(
        RejectionReasons.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );

    const matches = this.def.transitions.filter(
      (t) => t.from === snapshot.state && t.trigger === trigger,
    );
    if (matches.length === 0)
      return rejected(
        RejectionReasons.NoTransition,
        `No transition from '${snapshot.state}' on '${trigger}'.`,
      );

    try {
      // A guard is hand-written per runtime and may throw on unexpected input, so evaluate it inside the
      // totality backstop: a throwing guard degrades to internal-error rather than escaping advance (PD4).
      const chosen = matches.find((t) =>
        guardPasses(t, snapshot.context, input),
      );
      if (!chosen) {
        // An output no OnDone edge accepts has nowhere to go; it is not a guard a caller could satisfy by
        // sending something else, so it is a no-transition, as on the server.
        if (this.outcomeTriggers.has(trigger))
          return rejected(
            RejectionReasons.NoTransition,
            `No OnDone edge from '${snapshot.state}' accepts the train's output.`,
          );
        const message =
          matches.map((m) => m.guardMessage).find((m) => m != null) ??
          `A transition from '${snapshot.state}' on '${trigger}' exists but its guard rejected the input.`;
        return rejected(RejectionReasons.GuardFailed, message);
      }

      // The chosen edge is the single source of BOTH the destination and the reducer.
      const toState = chosen.to;
      const newContext = chosen.reduce
        ? chosen.reduce(snapshot.context, input)
        : clone(snapshot.context);
      const contextError = this.validateContext(toState, newContext);
      if (contextError != null)
        return rejected(RejectionReasons.InvalidContext, contextError);

      return {
        outcome: "transitioned",
        snapshot: {
          machine: this.def.id,
          version: this.def.version,
          state: toState,
          context: newContext,
        },
      };
    } catch (e) {
      // Totality backstop: the engine must never surface an exception to a caller.
      return rejected(RejectionReasons.InternalError, errorMessage(e));
    }
  }

  rehydrate(json: string): RehydrationResult {
    // Totality backstop: any parse/validation path that throws degrades to a typed Error here.
    try {
      return this.rehydrateCore(json);
    } catch (e) {
      return error(RehydrationErrorCodes.Malformed, errorMessage(e));
    }
  }

  private rehydrateCore(json: string): RehydrationResult {
    let node: unknown;
    try {
      node = JSON.parse(json);
    } catch {
      return error(RehydrationErrorCodes.Malformed, "Input is not valid JSON.");
    }

    if (typeof node !== "object" || node === null || Array.isArray(node))
      return error(
        RehydrationErrorCodes.Malformed,
        "Snapshot must be a JSON object.",
      );

    const obj = node as Record<string, unknown>;
    const machine = typeof obj.machine === "string" ? obj.machine : null;
    const stateToken = typeof obj.state === "string" ? obj.state : null;
    const version = asInt(obj.version);
    const context = isPlainObject(obj.context)
      ? (obj.context as Record<string, unknown>)
      : null;

    if (
      machine === null ||
      stateToken === null ||
      version === null ||
      context === null
    )
      return error(
        RehydrationErrorCodes.Malformed,
        "Snapshot is missing required fields (machine, version, state, context).",
      );

    if (machine !== this.def.id)
      return error(
        RehydrationErrorCodes.UnknownMachine,
        `Snapshot machine '${machine}' does not match '${this.def.id}'.`,
      );

    let effState = stateToken;
    let effContext = clone(context);
    let effVersion = version;

    if (effVersion > this.def.version)
      return error(
        RehydrationErrorCodes.VersionMismatch,
        `Snapshot version ${effVersion} is newer than definition version ${this.def.version}.`,
      );

    while (effVersion < this.def.version) {
      const migrate = this.def.migrations?.[effVersion];
      if (!migrate)
        return error(
          RehydrationErrorCodes.VersionMismatch,
          `No migration from version ${effVersion} to ${this.def.version}.`,
        );
      const migrated = migrate(effState, effContext);
      effState = migrated.state;
      effContext = migrated.context;
      effVersion++;
    }

    if (!this.stateSet.has(effState))
      return error(
        RehydrationErrorCodes.UnknownState,
        `Unknown state '${effState}'.`,
      );

    const contextError = this.validateContext(effState, effContext);
    if (contextError != null)
      return error(RehydrationErrorCodes.InvalidContext, contextError);

    return {
      result: "ok",
      snapshot: {
        machine,
        version: this.def.version,
        state: effState,
        context: effContext,
      },
    };
  }

  /** Canonical JSON: fixed envelope order, context keys sorted (RFC 8785, ordinal), recursive. */
  serialize(snapshot: Snapshot): string {
    return JSON.stringify({
      machine: snapshot.machine,
      version: snapshot.version,
      state: snapshot.state,
      context: canonicalize(snapshot.context),
    });
  }

  canFire(snapshot: Snapshot, trigger: string, input?: unknown): boolean {
    if (!this.stateSet.has(snapshot.state)) return false;
    // An outcome is not an action a caller fires, as on the server; `advance` still applies one.
    if (this.outcomeTriggers.has(trigger)) return false;
    return this.def.transitions.some(
      (t) =>
        t.from === snapshot.state &&
        t.trigger === trigger &&
        guardPasses(t, snapshot.context, input),
    );
  }

  availableTriggers(snapshot: Snapshot): string[] {
    if (!this.stateSet.has(snapshot.state)) return [];
    // An outcome trigger is something the server applies when a train finishes, never an action to offer.
    const seen = new Set<string>();
    for (const t of this.def.transitions)
      if (t.from === snapshot.state && !this.outcomeTriggers.has(t.trigger))
        seen.add(t.trigger);
    return [...seen];
  }

  /** The language-neutral structure (sorted ordinally) — the value the cross-language golden compares. */
  describe(): {
    id: string;
    version: number;
    initialState: string;
    states: string[];
    triggers: string[];
    transitions: { from: string; trigger: string; to: string }[];
  } {
    const states = [...this.def.states].sort(ordinal);
    const triggers = [
      ...new Set(this.def.transitions.map((t) => t.trigger)),
    ].sort(ordinal);
    const transitions = this.def.transitions
      .map((t) => ({
        from: t.from,
        trigger: t.trigger,
        to: t.to,
      }))
      .sort(
        (a, b) =>
          ordinal(a.from, b.from) ||
          ordinal(a.trigger, b.trigger) ||
          ordinal(a.to, b.to),
      );
    return {
      id: this.def.id,
      version: this.def.version,
      initialState: this.def.initialState,
      states,
      triggers,
      transitions,
    };
  }

  private validateContext(
    state: string,
    context: Record<string, unknown>,
  ): string | null {
    const validate = this.def.contextValidators?.[state as S];
    return validate ? validate(context) : null;
  }
}

function rejected(reason: string, detail: string): AdvanceResult {
  return { outcome: "rejected", reason, detail };
}

function error(code: string, message: string): RehydrationResult {
  return { result: "error", code, message };
}

function guardPasses<S extends string, T extends string>(
  t: TransitionDefinition<S, T>,
  context: Record<string, unknown>,
  input: unknown,
): boolean {
  return t.guard == null || t.guard(context, input);
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Accept any INTEGRAL JSON number in int32 range, matching C# (which widens for exactly this reason).
 * JSON has one number type, so 1, 1.0 and 1e0 are indistinguishable after parse; a non-integral or
 * out-of-range number is "missing/invalid", never a throw.
 */
function asInt(value: unknown): number | null {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    !Number.isInteger(value)
  )
    return null;
  if (value < -2147483648 || value > 2147483647) return null;
  return value;
}

/** The reference clone is a JSON round-trip — correct for the JSON-only snapshot contract. */
function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

/** RFC 8785 §3.2.3: sort object members by UTF-16 code unit (ordinal), recursively. Arrays keep order. */
function canonicalize(node: unknown): unknown {
  if (Array.isArray(node)) return node.map(canonicalize);
  if (node !== null && typeof node === "object") {
    const sorted: Record<string, unknown> = {};
    for (const key of Object.keys(node as Record<string, unknown>).sort(
      ordinal,
    ))
      sorted[key] = canonicalize((node as Record<string, unknown>)[key]);
    return sorted;
  }
  return node;
}

/** UTF-16 code-unit comparison — the JS default, equal to C# StringComparer.Ordinal. */
function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
