/**
 * The language-neutral, serializable state of a machine instance — the only thing that crosses the
 * wire or lands in a database row. The C# backend (Trax.Effect.StateMachine) reads and writes this
 * exact shape, and the shared conformance fixtures prove the two engines agree.
 */
export interface Snapshot {
  machine: string;
  version: number;
  state: string;
  context: Record<string, unknown>;
}

export type Guard = (context: Record<string, unknown>, input: unknown) => boolean;
export type Reduce = (context: Record<string, unknown>, input: unknown) => Record<string, unknown>;
export type ContextValidator = (context: Record<string, unknown>) => string | null;
/** Checks a trigger's input against the schema it declares: null when it matches, else a message. */
export type InputValidator = (input: unknown) => string | null;

/**
 * One edge: "from `from`, on `trigger`, go to `to`". `guard`/`reduce` are named code, never serialized
 * expressions — the snapshot carries structure and data, never logic.
 */
export interface TransitionDefinition<S extends string, T extends string> {
  from: S;
  trigger: T;
  to: S;
  guard?: Guard;
  /** Non-contract detail text surfaced as the rejection detail when this guard declines. */
  guardMessage?: string;
  reduce?: Reduce;
}

/**
 * A state's timer, as the machine declares it: on `trigger` the state moves to `target` once the duration
 * has passed. The duration is either fixed (`after`, an ISO-8601 duration) or read from a context field at
 * entry, bounded by `min` and `max` (ISO-8601 durations). The twin never fires a timer and holds no entry
 * time, so it reports no due time: the server sends that beside the snapshot.
 */
export interface MachineTimer {
  trigger: string;
  target: string;
  after?: string;
  field?: { name: string; min: string; max: string };
}

/** The result of migrating a snapshot one version forward. */
export interface MigrationResult {
  state: string;
  context: Record<string, unknown>;
}

/**
 * The static description of a machine. Lives in code, never in the snapshot. Kept in agreement with
 * the C# definition of the same machine by the shared conformance fixtures.
 */
export interface MachineDefinition<S extends string, T extends string> {
  id: string;
  version: number;
  initialState: S;
  createInitialContext: () => Record<string, unknown>;
  states: readonly S[];
  transitions: ReadonlyArray<TransitionDefinition<S, T>>;
  /**
   * States that are COMMITTED: once a draft reaches one, an irreversible effect has run and a soft
   * autosave must not overwrite it. Authored in C# via `.Committed()`, carried through the IR, and
   * enforced server-side by the persistence layer — mirrored here so a client can ask before it writes.
   */
  committedStates?: readonly S[];
  /**
   * The outcome triggers of states that invoke a train (`Fetching.done`, `Fetching.failed`,
   * `Fetching.cancelled`). Their edges are in `transitions`, tried in declaration order; when none accepts
   * a train's output the result is `no-transition`, as on the server, and they are never offered by
   * `availableTriggers`. Only `applyOutcome` applies one (`advance` refuses it as `outcome-bound`); the twin
   * applies an outcome it is told about and never starts a train.
   */
  outcomeTriggers?: readonly string[];
  /**
   * The timers of timed states, keyed by the timed state. Only the server fires one, through `applyTimer` here as
   * there: `advance` refuses a timer trigger as `timer-bound`, `canFire` is false for it and `availableTriggers`
   * never offers it. A transition into a state whose timer reads its duration from the context refuses a duration
   * the context does not hold within bounds as `invalid-input`, as the server does.
   */
  timers?: Readonly<Partial<Record<S, MachineTimer>>>;
  /**
   * Per-trigger input validators (outcome triggers included), run after an edge is found and before any guard
   * reads the input; a failure is `invalid-input`. A trigger with none is not checked. `machineFromIr` gives
   * every trigger one, from the IR's input schemas, as the server does.
   */
  inputValidators?: Partial<Record<string, InputValidator>>;
  /** Per-state validators: return null when the context is legal for that state, else a message. */
  contextValidators?: Partial<Record<S, ContextValidator>>;
  /** Forward migrations keyed by the version they migrate FROM. */
  migrations?: Record<number, (state: string, context: Record<string, unknown>) => MigrationResult>;
}
