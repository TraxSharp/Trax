import { problemFromAdvance, type Problem } from './problem';
import { RefusalCode } from './results';
import type { InputArgs, MachineSpec, StateOf, TriggerOf, TypedMachine, TypedSnapshot } from './typed';

export interface MachineObserver<Spec extends MachineSpec> {
  onTransition?(from: StateOf<Spec>, trigger: string, to: StateOf<Spec>, snapshot: TypedSnapshot<Spec>): void;
  onRejected?(trigger: string, problem: Problem, snapshot: TypedSnapshot<Spec>): void;
  onInternalError?(trigger: string, detail: string, snapshot: TypedSnapshot<Spec>): void;
}

export interface ControllerOptions<Spec extends MachineSpec> {
  initial: TypedSnapshot<Spec> | (() => TypedSnapshot<Spec>);
  observer?: MachineObserver<Spec>;
}

export interface ControllerView<Spec extends MachineSpec> {
  snapshot: TypedSnapshot<Spec>;
  problem: Problem | null;
}

/**
 * A framework-free store over a {@link TypedMachine}, held in memory: it holds the current snapshot, performs
 * transitions and notifies subscribers, and stores nothing. A machine a server owns is driven through
 * `ServerMachineController` instead. Critically, a DECLINED action still emits a new
 * view (with `problem` set) so a UI re-renders and surfaces the rejection instead of silently swallowing it.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#driving-a-machine-the-client-owns
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
      this.emit();
      return true;
    }

    const problem = problemFromAdvance(result)!;
    if (result.reason === RefusalCode.InternalError)
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
