import { durationTicks } from "./duration";
import {
  RehydrationErrorCodes,
  RejectionReasons,
  type AdvanceResult,
  type RehydrationResult,
} from "./results";
import type {
  MachineDefinition,
  MachineTimer,
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
 *
 * One engine contract with the server: `advance` takes only the triggers a caller sends, an invoked train's
 * outcome goes through `applyOutcome`, and a state's timer through `applyTimer`. Refusals, first match wins: an
 * unknown state (`no-transition`); an outcome trigger (`outcome-bound`); a timer trigger (`timer-bound`); no edge
 * from the state (`no-transition`); an input that does not match the trigger's declared schema (`invalid-input`);
 * a guard that declines (`guard-failed`); a target whose timer reads a duration the successor context does not hold
 * within bounds (`invalid-input`); a successor context the target refuses (`invalid-context`).
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#the-engine-and-the-typed-view
 */
export class SnapshotMachine<S extends string, T extends string> {
  private readonly stateSet: ReadonlySet<string>;
  private readonly outcomeTriggers: ReadonlySet<string>;
  private readonly timerTriggers: ReadonlySet<string>;

  constructor(private readonly def: MachineDefinition<S, T>) {
    this.stateSet = new Set<string>(def.states);
    this.outcomeTriggers = new Set<string>(def.outcomeTriggers ?? []);
    this.timerTriggers = new Set<string>(
      Object.values<MachineTimer | undefined>(def.timers ?? {}).flatMap((t) =>
        t ? [t.trigger] : [],
      ),
    );
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
    // An outcome records what an invoked run produced; from a caller, its input would be whatever was sent.
    if (this.outcomeTriggers.has(trigger))
      return rejected(
        RejectionReasons.OutcomeBound,
        "This step is completed by the outcome of its work, not by an action.",
      );
    // A timer is the server acting once a state has been held long enough; no caller fires it.
    if (this.timerTriggers.has(trigger))
      return rejected(
        RejectionReasons.TimerBound,
        "This step moves on by itself when its time is up, not by an action.",
      );

    const matches = this.def.transitions.filter(
      (t) => t.from === snapshot.state && t.trigger === trigger,
    );
    if (matches.length === 0)
      return rejected(
        RejectionReasons.NoTransition,
        `No transition from '${snapshot.state}' on '${trigger}'.`,
      );

    // The input is checked against the trigger's declared schema before any guard reads it.
    const inputError = this.inputProblem(trigger, input);
    if (inputError != null)
      return rejected(RejectionReasons.InvalidInput, inputError);

    try {
      // A guard is hand-written per runtime and may throw on unexpected input, so evaluate it inside the
      // totality backstop: a throwing guard degrades to internal-error rather than escaping advance (PD4).
      const chosen = matches.find((t) =>
        guardPasses(t, snapshot.context, input),
      );
      if (!chosen) {
        const message =
          matches.map((m) => m.guardMessage).find((m) => m != null) ??
          `A transition from '${snapshot.state}' on '${trigger}' exists but its guard rejected the input.`;
        return rejected(RejectionReasons.GuardFailed, message);
      }

      return this.take(snapshot, chosen, input);
    } catch (e) {
      // Totality backstop: the engine must never surface an exception to a caller.
      return rejected(RejectionReasons.InternalError, errorMessage(e));
    }
  }

  /**
   * Applies an invoked train's outcome (`Fetching.done`, `.failed`, `.cancelled`) with the train's output as
   * its input, the way the server applies a run's outcome. The separate entry point for outcomes: `advance` is
   * for the triggers a caller sends, and refuses these. A success tries the state's `OnDone` edges in
   * declaration order and takes the first whose guard accepts the output; an output no edge accepts, and an
   * outcome of a state the snapshot is not in, are `no-transition`. An output that does not match the train's
   * output schema is `invalid-input`. Never throws.
   */
  applyOutcome(
    snapshot: Snapshot,
    trigger: string,
    output?: unknown,
  ): AdvanceResult {
    if (!this.stateSet.has(snapshot.state))
      return rejected(
        RejectionReasons.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );
    if (!this.outcomeTriggers.has(trigger))
      return rejected(
        RejectionReasons.NoTransition,
        `'${trigger}' is not an outcome of this machine.`,
      );

    const matches = this.def.transitions.filter(
      (t) => t.from === snapshot.state && t.trigger === trigger,
    );
    if (matches.length === 0)
      return rejected(
        RejectionReasons.NoTransition,
        `No transition from '${snapshot.state}' on '${trigger}'.`,
      );

    const outputError = this.inputProblem(trigger, output);
    if (outputError != null)
      return rejected(RejectionReasons.InvalidInput, outputError);

    try {
      const chosen = matches.find((t) =>
        guardPasses(t, snapshot.context, output),
      );
      // An output no OnDone edge accepts has nowhere to go; it is not a guard a caller could satisfy by sending
      // something else, so it is a no-transition, as on the server.
      if (!chosen)
        return rejected(
          RejectionReasons.NoTransition,
          `No OnDone edge from '${snapshot.state}' accepts the train's output.`,
        );
      return this.take(snapshot, chosen, output);
    } catch (e) {
      return rejected(RejectionReasons.InternalError, errorMessage(e));
    }
  }

  // The chosen edge is the single source of BOTH the destination and the reducer.
  private take(
    snapshot: Snapshot,
    chosen: TransitionDefinition<S, T>,
    input: unknown,
  ): AdvanceResult {
    const newContext = chosen.reduce
      ? chosen.reduce(snapshot.context, input)
      : clone(snapshot.context);
    return this.enter(chosen.to, newContext);
  }

  // The successor of a transition into toState: the target's timer reads its duration from the context first, so a
  // duration the context does not hold within bounds is refused as input before the context is validated, as on the
  // server; then the target's validator.
  private enter(toState: string, newContext: Record<string, unknown>): AdvanceResult {
    const timerError = this.timerProblem(toState, newContext);
    if (timerError != null)
      return rejected(RejectionReasons.InvalidInput, timerError);

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
  }

  // Why entering `state` with `context` cannot arm its timer: the duration field is missing, not an ISO-8601
  // duration, or outside the declared bounds. Null when the state has no timer, a fixed one, or a duration in bounds.
  private timerProblem(state: string, context: Record<string, unknown>): string | null {
    const field = this.def.timers?.[state as S]?.field;
    if (!field) return null;
    const ticks = durationTicks(context[field.name]);
    if (ticks == null)
      return `The timer of ${state} reads '${field.name}', which must hold an ISO-8601 duration such as "PT5M".`;
    const min = durationTicks(field.min);
    const max = durationTicks(field.max);
    if (min == null || max == null || ticks < min || ticks > max)
      return `The timer of ${state} waits ${String(context[field.name])}, outside its bounds of ${field.min} to ${field.max}.`;
    return null;
  }

  // Why the input does not match the trigger's declared schema, or null. A validator that throws refuses.
  private inputProblem(trigger: string, input: unknown): string | null {
    const validate = this.def.inputValidators?.[trigger];
    if (!validate) return null;
    try {
      return validate(input);
    } catch {
      return "The input could not be read.";
    }
  }

  /**
   * Fires the timer `trigger` (`<State>.after`) of the state the snapshot is in, the way the server applies a due
   * timer: the context is carried to the timer's target unchanged. The separate entry point for timers: `advance`
   * refuses them. A trigger that is not the timer of the snapshot's state is `no-transition`; a target whose own
   * timer reads a duration the context does not hold within bounds is `invalid-input`; a target that refuses the
   * context is `invalid-context`. The twin never fires a timer on its own: it holds no entry time. Never throws.
   */
  applyTimer(snapshot: Snapshot, trigger: string): AdvanceResult {
    if (!this.stateSet.has(snapshot.state))
      return rejected(
        RejectionReasons.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );
    const timer = this.def.timers?.[snapshot.state as S];
    if (!timer || timer.trigger !== trigger)
      return rejected(
        RejectionReasons.NoTransition,
        `No timer fires from '${snapshot.state}' on '${trigger}'.`,
      );
    try {
      return this.enter(timer.target, clone(snapshot.context));
    } catch (e) {
      return rejected(RejectionReasons.InternalError, errorMessage(e));
    }
  }

  /**
   * The timer declared on the snapshot's state, or null when the state has none: its trigger, its target and its
   * declared duration (fixed `after`, or the context `field` with its bounds). Reports no due time: the twin holds
   * no entry time, so the server sends that beside the snapshot.
   */
  timer(snapshot: Snapshot): MachineTimer | null {
    const timer = this.def.timers?.[snapshot.state as S];
    if (!timer) return null;
    return {
      trigger: timer.trigger,
      target: timer.target,
      ...(timer.after !== undefined ? { after: timer.after } : {}),
      ...(timer.field !== undefined ? { field: { ...timer.field } } : {}),
    };
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
    // An outcome or a timer is not an action a caller fires, as on the server.
    if (this.outcomeTriggers.has(trigger) || this.timerTriggers.has(trigger))
      return false;
    if (this.inputProblem(trigger, input) != null) return false;
    try {
      return this.def.transitions.some(
        (t) =>
          t.from === snapshot.state &&
          t.trigger === trigger &&
          guardPasses(t, snapshot.context, input),
      );
    } catch {
      return false;
    }
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
