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
  /** Per-state validators: return null when the context is legal for that state, else a message. */
  contextValidators?: Partial<Record<S, ContextValidator>>;
  /** Forward migrations keyed by the version they migrate FROM. */
  migrations?: Record<number, (state: string, context: Record<string, unknown>) => MigrationResult>;
}
