import type { SnapshotMachine } from './machine';
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
  constructor(readonly core: SnapshotMachine<StateOf<Spec>, TriggerOf<Spec>>) {}

  get id(): string {
    return this.core.definition.id;
  }

  get states(): StateOf<Spec>[] {
    return [...this.core.definition.states];
  }

  initial(): TypedSnapshot<Spec> {
    return this.core.createInitialSnapshot() as TypedSnapshot<Spec>;
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

// re-export the raw result types so consumers can build a Problem outside a controller if needed.
export type { AdvanceResult, RehydrationResult };
