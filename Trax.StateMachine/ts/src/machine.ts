import { durationTicks } from "./duration";
import {
  RefusalCode,
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
 * unknown state (`NO_TRANSITION`); an outcome trigger (`OUTCOME_BOUND`); a timer trigger (`TIMER_BOUND`); no edge
 * from the state (`NO_TRANSITION`); an input that does not match the trigger's declared schema (`INVALID_INPUT`);
 * a guard that declines (`GUARD_FAILED`); a target whose timer reads a duration the successor context does not hold
 * within bounds (`INVALID_INPUT`); a successor context the target refuses (`INVALID_CONTEXT`).
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
        RefusalCode.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );
    // An outcome records what an invoked run produced; from a caller, its input would be whatever was sent.
    if (this.outcomeTriggers.has(trigger))
      return rejected(
        RefusalCode.OutcomeBound,
        "This step is completed by the outcome of its work, not by an action.",
      );
    // A timer is the server acting once a state has been held long enough; no caller fires it.
    if (this.timerTriggers.has(trigger))
      return rejected(
        RefusalCode.TimerBound,
        "This step moves on by itself when its time is up, not by an action.",
      );

    const matches = this.def.transitions.filter(
      (t) => t.from === snapshot.state && t.trigger === trigger,
    );
    if (matches.length === 0)
      return rejected(
        RefusalCode.NoTransition,
        `No transition from '${snapshot.state}' on '${trigger}'.`,
      );

    // The input is checked against the trigger's declared schema before any guard reads it.
    const inputError = this.inputProblem(trigger, input);
    if (inputError != null)
      return rejected(RefusalCode.InvalidInput, inputError);

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
        return rejected(RefusalCode.GuardFailed, message);
      }

      return this.take(snapshot, chosen, input);
    } catch (e) {
      // Totality backstop: the engine must never surface an exception to a caller.
      return rejected(RefusalCode.InternalError, errorMessage(e));
    }
  }

  /**
   * Applies an invoked train's outcome (`Fetching.done`, `.failed`, `.cancelled`) with the train's output as
   * its input, the way the server applies a run's outcome. The separate entry point for outcomes: `advance` is
   * for the triggers a caller sends, and refuses these. A success tries the state's `OnDone` edges in
   * declaration order and takes the first whose guard accepts the output; an output no edge accepts, and an
   * outcome of a state the snapshot is not in, are `NO_TRANSITION`. An output that does not match the train's
   * output schema is `INVALID_INPUT`. Never throws.
   */
  applyOutcome(
    snapshot: Snapshot,
    trigger: string,
    output?: unknown,
  ): AdvanceResult {
    if (!this.stateSet.has(snapshot.state))
      return rejected(
        RefusalCode.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );
    if (!this.outcomeTriggers.has(trigger))
      return rejected(
        RefusalCode.NoTransition,
        `'${trigger}' is not an outcome of this machine.`,
      );

    const matches = this.def.transitions.filter(
      (t) => t.from === snapshot.state && t.trigger === trigger,
    );
    if (matches.length === 0)
      return rejected(
        RefusalCode.NoTransition,
        `No transition from '${snapshot.state}' on '${trigger}'.`,
      );

    const outputError = this.inputProblem(trigger, output);
    if (outputError != null)
      return rejected(RefusalCode.InvalidInput, outputError);

    try {
      const chosen = matches.find((t) =>
        guardPasses(t, snapshot.context, output),
      );
      // An output no OnDone edge accepts has nowhere to go; it is not a guard a caller could satisfy by sending
      // something else, so it is a no-transition, as on the server.
      if (!chosen)
        return rejected(
          RefusalCode.NoTransition,
          `No OnDone edge from '${snapshot.state}' accepts the train's output.`,
        );
      return this.take(snapshot, chosen, output);
    } catch (e) {
      return rejected(RefusalCode.InternalError, errorMessage(e));
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
  private enter(
    toState: string,
    newContext: Record<string, unknown>,
  ): AdvanceResult {
    const timerError = this.timerProblem(toState, newContext);
    if (timerError != null)
      return rejected(RefusalCode.InvalidInput, timerError);

    const contextError = this.validateContext(toState, newContext);
    if (contextError != null)
      return rejected(RefusalCode.InvalidContext, contextError);

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
  private timerProblem(
    state: string,
    context: Record<string, unknown>,
  ): string | null {
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
   * refuses them. A trigger that is not the timer of the snapshot's state is `NO_TRANSITION`; a target whose own
   * timer reads a duration the context does not hold within bounds is `INVALID_INPUT`; a target that refuses the
   * context is `INVALID_CONTEXT`. The twin never fires a timer on its own: it holds no entry time. Never throws.
   */
  applyTimer(snapshot: Snapshot, trigger: string): AdvanceResult {
    if (!this.stateSet.has(snapshot.state))
      return rejected(
        RefusalCode.NoTransition,
        `Unknown state '${snapshot.state}'.`,
      );
    const timer = this.def.timers?.[snapshot.state as S];
    if (!timer || timer.trigger !== trigger)
      return rejected(
        RefusalCode.NoTransition,
        `No timer fires from '${snapshot.state}' on '${trigger}'.`,
      );
    try {
      return this.enter(timer.target, clone(snapshot.context));
    } catch (e) {
      return rejected(RefusalCode.InternalError, errorMessage(e));
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

  /**
   * Reads a stored snapshot. Pass `{ fromServer: true }` only for a snapshot the server sent: its context is then
   * trusted on a custom rule this twin has no handler for (the server already evaluated it), and still checked for
   * its shape and every built-in rule. A snapshot from anywhere else is checked in full.
   */
  rehydrate(
    json: string,
    options?: { fromServer?: boolean },
  ): RehydrationResult {
    // Totality backstop: any parse/validation path that throws degrades to a typed Error here.
    try {
      return this.rehydrateCore(json, options?.fromServer === true);
    } catch (e) {
      return error(RefusalCode.Malformed, errorMessage(e));
    }
  }

  private rehydrateCore(
    json: string,
    fromServer: boolean,
  ): RehydrationResult {
    let node: unknown;
    try {
      node = JSON.parse(json);
    } catch {
      return error(RefusalCode.Malformed, "Input is not valid JSON.");
    }

    if (typeof node !== "object" || node === null || Array.isArray(node))
      return error(
        RefusalCode.Malformed,
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
        RefusalCode.Malformed,
        "Snapshot is missing required fields (machine, version, state, context).",
      );

    if (machine !== this.def.id)
      return error(
        RefusalCode.UnknownMachine,
        `Snapshot machine '${machine}' does not match '${this.def.id}'.`,
      );

    let effState = stateToken;
    let effContext = clone(context);
    let effVersion = version;

    if (effVersion > this.def.version)
      return error(
        RefusalCode.VersionMismatch,
        `Snapshot version ${effVersion} is newer than definition version ${this.def.version}.`,
      );

    while (effVersion < this.def.version) {
      const migrate = this.def.migrations?.[effVersion];
      if (!migrate)
        return error(
          RefusalCode.VersionMismatch,
          `No migration from version ${effVersion} to ${this.def.version}.`,
        );
      const migrated = migrate(effState, effContext);
      effState = migrated.state;
      effContext = migrated.context;
      effVersion++;
    }

    if (!this.stateSet.has(effState))
      return error(
        RefusalCode.UnknownState,
        `Unknown state '${effState}'.`,
      );

    const contextError = this.validateContext(
      effState,
      effContext,
      fromServer,
    );
    if (contextError != null)
      return error(RefusalCode.InvalidContext, contextError);

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
    return (
      `{"machine":${JSON.stringify(snapshot.machine)}` +
      `,"version":${JSON.stringify(snapshot.version)}` +
      `,"state":${JSON.stringify(snapshot.state)}` +
      `,"context":${canonicalJson(snapshot.context)}}`
    );
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
    fromServer = false,
  ): string | null {
    const validate = this.def.contextValidators?.[state as S];
    if (!validate) return null;
    return fromServer ? validate(context, { fromServer }) : validate(context);
  }
}

function rejected(reason: RefusalCode, detail: string): AdvanceResult {
  return { outcome: "rejected", reason, detail };
}

function error(code: RefusalCode, message: string): RehydrationResult {
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

/**
 * RFC 8785 §3.2.3: object members sorted by UTF-16 code unit (ordinal), recursively; arrays keep order. The text is
 * written member by member, because an object built in sorted order would still enumerate integer-like keys first
 * and would turn a "__proto__" key into its prototype.
 */
function canonicalJson(node: unknown): string {
  return canonicalValue(node, "") ?? "null";
}

// A value as JSON.stringify writes it, its members in canonical order: `toJSON` is called first, a hole or a function in
// an array is null, and a function, symbol or undefined member is left out. Undefined when the value is not written.
function canonicalValue(node: unknown, key: string): string | undefined {
  if (
    node !== null &&
    (typeof node === "object" || typeof node === "bigint") &&
    typeof (node as { toJSON?: unknown }).toJSON === "function"
  )
    node = (node as { toJSON(key: string): unknown }).toJSON(key);
  if (node === undefined || typeof node === "function" || typeof node === "symbol")
    return undefined;
  if (Array.isArray(node)) {
    const items: string[] = [];
    for (let i = 0; i < node.length; i++)
      items.push(canonicalValue(node[i], String(i)) ?? "null");
    return `[${items.join(",")}]`;
  }
  if (
    node !== null &&
    typeof node === "object" &&
    !(node instanceof Number || node instanceof String || node instanceof Boolean)
  ) {
    const record = node as Record<string, unknown>;
    const members: string[] = [];
    for (const member of Object.keys(record).sort(ordinal)) {
      const value = canonicalValue(record[member], member);
      if (value !== undefined) members.push(`${JSON.stringify(member)}:${value}`);
    }
    return `{${members.join(",")}}`;
  }
  return JSON.stringify(node);
}

/** UTF-16 code-unit comparison — the JS default, equal to C# StringComparer.Ordinal. */
function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
