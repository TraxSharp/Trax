import { SnapshotMachine } from './machine';
import { machineFromIr, type IrDocument } from './rules/irMachine';
import type { AdvanceResult, RehydrationResult } from './results';

/**
 * A compile-time description of a machine: each state's context type and each trigger's input type.
 * Wrapping the raw {@link SnapshotMachine} in a {@link TypedMachine}<Spec> gives consumers typed
 * `state`/`context`/`send`/`can` — `context` is discriminated by `state`, and a trigger's input is
 * required-or-forbidden at the type level.
 */
export interface MachineSpec {
  states: Record<string, Record<string, unknown>>;
  triggers: Record<string, unknown>;
}

export type StateOf<Spec extends MachineSpec> = keyof Spec['states'] & string;
export type TriggerOf<Spec extends MachineSpec> = keyof Spec['triggers'] & string;
export type ContextOf<Spec extends MachineSpec, S extends StateOf<Spec>> = Spec['states'][S];
export type InputOf<Spec extends MachineSpec, T extends TriggerOf<Spec>> = Spec['triggers'][T];

/** The snapshot as a discriminated union: on each state, `context` is exactly that state's shape. */
export type TypedSnapshot<Spec extends MachineSpec> = {
  [S in StateOf<Spec>]: { machine: string; version: number; state: S; context: Spec['states'][S] };
}[StateOf<Spec>];

/** A trigger with `undefined` input takes no argument; otherwise the input is required. */
export type InputArgs<Spec extends MachineSpec, T extends TriggerOf<Spec>> = Spec['triggers'][T] extends undefined
  ? []
  : [input: Spec['triggers'][T]];

export type TypedAdvanceResult<Spec extends MachineSpec> =
  | { outcome: 'transitioned'; snapshot: TypedSnapshot<Spec> }
  | { outcome: 'rejected'; reason: string; detail?: string };

export type TypedRehydrationResult<Spec extends MachineSpec> =
  | { result: 'ok'; snapshot: TypedSnapshot<Spec> }
  | { result: 'error'; code: string; message: string };

/** A thin, types-only wrapper over the raw engine. Adds no behavior — just compile-time safety. */
export class TypedMachine<Spec extends MachineSpec> {
  /**
   * @param schemaHash SHA-256 of the machine's IR (the same value C#'s `IMachine.SchemaHash` computes),
   * embedded in the generated twin so the client can send it for the server-side version-skew handshake.
   * `''` when the twin was built without one.
   */
  constructor(
    readonly core: SnapshotMachine<StateOf<Spec>, TriggerOf<Spec>>,
    readonly schemaHash: string = '',
  ) {}

  get id(): string {
    return this.core.definition.id;
  }

  get states(): StateOf<Spec>[] {
    return [...this.core.definition.states];
  }

  /** The definition's schema version — the `version` every snapshot this machine writes must carry. */
  get version(): number {
    return this.core.definition.version;
  }

  initial(): TypedSnapshot<Spec> {
    return this.core.createInitialSnapshot() as TypedSnapshot<Spec>;
  }

  /**
   * A snapshot pinned at an arbitrary state — for a client whose UI owns the draft values (a form, say)
   * and rebuilds the snapshot to ask the machine a question, rather than letting the machine own them.
   * Takes `machine` and `version` from the definition, so a `.MigrateFrom` version bump can't leave a
   * caller writing snapshots stamped with a stale version.
   */
  snapshotAt<S extends StateOf<Spec>>(state: S, context: Spec['states'][S]): TypedSnapshot<Spec> {
    return {
      machine: this.id,
      version: this.version,
      state,
      context,
    } as TypedSnapshot<Spec>;
  }

  /**
   * Whether `state` is COMMITTED — an irreversible effect has run there, so the draft must not be
   * overwritten by a soft save. The set comes from the machine's own IR (`.Committed()` in C#), so a
   * caller never has to hardcode the terminal state's name.
   */
  isCommitted(state: StateOf<Spec>): boolean {
    return this.core.definition.committedStates?.includes(state) ?? false;
  }

  advance<T extends TriggerOf<Spec>>(
    snapshot: TypedSnapshot<Spec>,
    trigger: T,
    ...input: InputArgs<Spec, T>
  ): TypedAdvanceResult<Spec> {
    return this.core.advance(snapshot, trigger, input[0]) as TypedAdvanceResult<Spec>;
  }

  rehydrate(json: string): TypedRehydrationResult<Spec> {
    return this.core.rehydrate(json) as TypedRehydrationResult<Spec>;
  }

  serialize(snapshot: TypedSnapshot<Spec>): string {
    return this.core.serialize(snapshot);
  }

  can<T extends TriggerOf<Spec>>(snapshot: TypedSnapshot<Spec>, trigger: T, ...input: InputArgs<Spec, T>): boolean {
    return this.core.canFire(snapshot, trigger, input[0]);
  }

  available(snapshot: TypedSnapshot<Spec>): TriggerOf<Spec>[] {
    return this.core.availableTriggers(snapshot) as TriggerOf<Spec>[];
  }
}

/**
 * Build a {@link TypedMachine}<Spec> straight from an IR document. `machineFromIr` is data-driven, so it can
 * only produce a `SnapshotMachine<string, string>`; the single unavoidable narrowing to the Spec's literal
 * state/trigger unions is confined here so generated twins (and hand callers) never emit the `as unknown as`
 * themselves. Safe by construction: the codegen emits the Spec and the IR from the same C# source, and the
 * drift tests pin the two together.
 */
export function typedMachineFromIr<Spec extends MachineSpec>(
  ir: IrDocument,
  schemaHash = '',
): TypedMachine<Spec> {
  const core = new SnapshotMachine(machineFromIr(ir)) as unknown as SnapshotMachine<
    StateOf<Spec>,
    TriggerOf<Spec>
  >;
  return new TypedMachine<Spec>(core, schemaHash);
}

// re-export the raw result types so consumers can build a Problem outside a controller if needed.
export type { AdvanceResult, RehydrationResult };
