import { problemFromAdvance, type Problem } from './problem';
import { RejectionReasons } from './results';
import type { InputArgs, MachineSpec, StateOf, TriggerOf, TypedMachine, TypedSnapshot } from './typed';

export interface MachineObserver<Spec extends MachineSpec> {
  onTransition?(from: StateOf<Spec>, trigger: string, to: StateOf<Spec>, snapshot: TypedSnapshot<Spec>): void;
  onRejected?(trigger: string, problem: Problem, snapshot: TypedSnapshot<Spec>): void;
  onInternalError?(trigger: string, detail: string, snapshot: TypedSnapshot<Spec>): void;
}

export interface ControllerOptions<Spec extends MachineSpec> {
  initial: TypedSnapshot<Spec> | (() => TypedSnapshot<Spec>);
  /** Runs after every successful transition — persistence is transition-driven, never a decoupled timer. */
  persist?(snapshot: TypedSnapshot<Spec>): void | Promise<void>;
  observer?: MachineObserver<Spec>;
}

export interface ControllerView<Spec extends MachineSpec> {
  snapshot: TypedSnapshot<Spec>;
  problem: Problem | null;
}

/**
 * A framework-free store over a {@link TypedMachine}. Holds the current snapshot, performs transitions,
 * persists after each success, and notifies subscribers. Critically, a DECLINED action still emits a new
 * view (with `problem` set) so a UI re-renders and surfaces the rejection instead of silently swallowing it.
 */
export class MachineController<Spec extends MachineSpec> {
  private view: ControllerView<Spec>;
  private readonly listeners = new Set<() => void>();

  constructor(
    private readonly machine: TypedMachine<Spec>,
    private readonly options: ControllerOptions<Spec>,
  ) {
    const initial = typeof options.initial === 'function' ? options.initial() : options.initial;
    this.view = { snapshot: initial, problem: null };
  }

  getSnapshot(): ControllerView<Spec> {
    return this.view;
  }

  get snapshot(): TypedSnapshot<Spec> {
    return this.view.snapshot;
  }

  get state(): StateOf<Spec> {
    return this.view.snapshot.state;
  }

  get context(): TypedSnapshot<Spec>['context'] {
    return this.view.snapshot.context;
  }

  get lastProblem(): Problem | null {
    return this.view.problem;
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  send<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean {
    const result = this.machine.advance(this.view.snapshot, trigger, ...input);

    if (result.outcome === 'transitioned') {
      const from = this.view.snapshot.state;
      this.view = { snapshot: result.snapshot, problem: null };
      this.options.observer?.onTransition?.(from, trigger, result.snapshot.state, result.snapshot);
      void this.options.persist?.(result.snapshot);
      this.emit();
      return true;
    }

    const problem = problemFromAdvance(result)!;
    if (result.reason === RejectionReasons.InternalError)
      this.options.observer?.onInternalError?.(trigger, result.detail ?? '', this.view.snapshot);
    else this.options.observer?.onRejected?.(trigger, problem, this.view.snapshot);

    // Emit a NEW view object (same snapshot) so a store like useSyncExternalStore re-renders and the UI
    // can show `problem` — a rejection is surfaced, never dropped.
    this.view = { snapshot: this.view.snapshot, problem };
    this.emit();
    return false;
  }

  can<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean {
    return this.machine.can(this.view.snapshot, trigger, ...input);
  }

  available(): TriggerOf<Spec>[] {
    return this.machine.available(this.view.snapshot);
  }

  reset(next?: TypedSnapshot<Spec>): void {
    this.view = { snapshot: next ?? this.machine.initial(), problem: null };
    this.emit();
  }

  private emit(): void {
    for (const listener of this.listeners) listener();
  }
}
