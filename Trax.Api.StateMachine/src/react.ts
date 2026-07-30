import { useRef, useSyncExternalStore } from 'react';
import { MachineController, type ControllerOptions, type MachineObserver } from './controller';
import { createDevLogger, type DevLoggerOptions } from './devtools';
import type { Problem } from './problem';
import type { InputArgs, MachineSpec, StateOf, TriggerOf, TypedMachine, TypedSnapshot } from './typed';

export interface UseMachineOptions<Spec extends MachineSpec> extends ControllerOptions<Spec> {
  /** Log transitions/rejections in dev; `{ strict: true }` throws on a real internal error (fail fast). */
  devtools?: boolean | DevLoggerOptions;
}

export interface UseMachineResult<Spec extends MachineSpec> {
  snapshot: TypedSnapshot<Spec>;
  state: StateOf<Spec>;
  context: TypedSnapshot<Spec>['context'];
  send<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean;
  can<T extends TriggerOf<Spec>>(trigger: T, ...input: InputArgs<Spec, T>): boolean;
  available: TriggerOf<Spec>[];
  lastProblem: Problem | null;
  reset(next?: TypedSnapshot<Spec>): void;
}

/**
 * The whole React surface: owns the snapshot, persists after each step, re-renders on every change
 * (including a rejection, so `lastProblem` never goes unseen). Built over the framework-free
 * {@link MachineController} via `useSyncExternalStore`.
 */
export function useMachine<Spec extends MachineSpec>(
  machine: TypedMachine<Spec>,
  options: UseMachineOptions<Spec>,
): UseMachineResult<Spec> {
  const controllerRef = useRef<MachineController<Spec> | null>(null);
  if (controllerRef.current === null) {
    const { devtools, observer, ...rest } = options;
    const logger = devtools ? createDevLogger<Spec>(machine.id, devtools === true ? {} : devtools) : undefined;
    controllerRef.current = new MachineController<Spec>(machine, {
      ...rest,
      observer: composeObservers(observer, logger),
    });
  }
  const controller = controllerRef.current;

  const view = useSyncExternalStore(
    (listener) => controller.subscribe(listener),
    () => controller.getSnapshot(),
    () => controller.getSnapshot(),
  );

  return {
    snapshot: view.snapshot,
    state: view.snapshot.state,
    context: view.snapshot.context,
    send: controller.send.bind(controller) as UseMachineResult<Spec>['send'],
    can: controller.can.bind(controller) as UseMachineResult<Spec>['can'],
    available: controller.available(),
    lastProblem: view.problem,
    reset: controller.reset.bind(controller),
  };
}

function composeObservers<Spec extends MachineSpec>(
  a?: MachineObserver<Spec>,
  b?: MachineObserver<Spec>,
): MachineObserver<Spec> | undefined {
  if (!a) return b;
  if (!b) return a;
  return {
    onTransition: (from, trigger, to, snapshot) => {
      a.onTransition?.(from, trigger, to, snapshot);
      b.onTransition?.(from, trigger, to, snapshot);
    },
    onRejected: (trigger, problem, snapshot) => {
      a.onRejected?.(trigger, problem, snapshot);
      b.onRejected?.(trigger, problem, snapshot);
    },
    onInternalError: (trigger, detail, snapshot) => {
      a.onInternalError?.(trigger, detail, snapshot);
      b.onInternalError?.(trigger, detail, snapshot);
    },
  };
}
