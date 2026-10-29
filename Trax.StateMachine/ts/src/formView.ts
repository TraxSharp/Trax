/**
 * Driving a machine whose DATA lives outside it.
 *
 * {@link MachineController} assumes the machine owns the snapshot. That is the right model for a flow
 * with no other state, but a wizard built on a form library already has a source of truth — the form —
 * and wants the machine as an oracle instead: "given these values at this step, may I fire this, and
 * where does it land?" Every such call rebuilds a snapshot, asks, and throws the result away.
 *
 * Written by hand that is four near-identical wrappers, a step↔state map maintained in both directions,
 * and a snapshot envelope stamped by the caller. A {@link FormView} is all of that, derived from the two
 * things only the app can supply: which UI step means which machine state, and how its values project
 * onto the machine's context.
 */
import type { PlanResult } from './plan';
import type { SyncOptions, SyncOutcome } from './server/serverController';
import type {
  MachineSpec,
  StateOf,
  TriggerOf,
  TypedMachine,
  TypedSnapshot,
} from './typed';

/** Where a fired trigger left the UI, in the UI's own step names, or why the machine declined. */
export type StepAdvance<Step extends string> =
  | { ok: true; step: Step }
  | { ok: false; reason: string; detail?: string };

/** A stored snapshot, rehydrated and located in the UI's step vocabulary. */
export interface ResumedStep<Spec extends MachineSpec, Step extends string> {
  step: Step;
  snapshot: TypedSnapshot<Spec>;
}

export interface FormViewOptions<
  Spec extends MachineSpec,
  Step extends string,
  Values,
  Options,
> {
  /**
   * Each UI step's machine state. Give it once, in this direction only — the inverse is derived, so
   * the two can't drift. Must be one-to-one; two steps sharing a state would make the inverse
   * ambiguous and is rejected at construction.
   */
  steps: Readonly<Record<Step, StateOf<Spec>>>;
  /**
   * Project the UI's values onto the machine's context. This is the one part no library can write:
   * the form's shape is the app's business, the context's shape is the machine's, and this is where
   * they meet. Called with `options` possibly undefined, so give the parameter a default.
   */
  toContext: (values: Values, options?: Options) => Spec['states'][StateOf<Spec>];
  /**
   * The event that writes the form's values to the machine, for `plan` and `sync`: its trigger, and its input from the
   * values (`toContext`'s projection when no `input` is given). Without it, a plan writes no fields.
   */
  fieldsEvent?: {
    trigger: TriggerOf<Spec>;
    input?: (values: Values, options?: Options) => unknown;
  };
  /** The triggers, sent without input, that move between the steps, in the order `plan` and `sync` try them. */
  navigation?: readonly TriggerOf<Spec>[];
}

/** What `FormView.sync` drives: a `ServerMachineController`, or anything with its `syncTo`. */
export interface FormSyncTarget<Spec extends MachineSpec> {
  syncTo(target: StateOf<Spec>, options: SyncOptions<Spec>): Promise<SyncOutcome>;
}

export interface FormView<
  Spec extends MachineSpec,
  Step extends string,
  Values,
  Options,
> {
  /** The machine state a UI step means. */
  stateFor(step: Step): StateOf<Spec>;
  /** The UI step a machine state means — the derived inverse of `steps`. */
  stepFor(state: StateOf<Spec>): Step;
  /** The wire snapshot for these values at this step. */
  snapshotFor(step: Step, values: Values, options?: Options): TypedSnapshot<Spec>;
  /** Fire a trigger. On success, the step to move to — the machine picks it, including any branch. */
  advance(
    step: Step,
    values: Values,
    trigger: TriggerOf<Spec>,
    input?: unknown,
    options?: Options,
  ): StepAdvance<Step>;
  /** Whether firing `trigger` now would succeed — for enabling or disabling a control. */
  can(
    step: Step,
    values: Values,
    trigger: TriggerOf<Spec>,
    input?: unknown,
    options?: Options,
  ): boolean;
  /** Canonical JSON for these values at this step — what an autosave stores. */
  serialize(step: Step, values: Values, options?: Options): string;
  /**
   * Locate a stored snapshot in the UI's steps, or null when there is nothing to resume: no JSON, a
   * snapshot that fails to rehydrate, or one in a COMMITTED state (a finished run doesn't resume into
   * its own terminal screen). Which states are committed comes from the machine, never a hardcoded name.
   */
  resume(json: string | null | undefined): ResumedStep<Spec, Step> | null;
  /**
   * Whether the stored snapshot is in a committed state. Distinct from `resume` returning null, which
   * also covers "no draft at all" — a caller that must reset a finished run needs to tell those apart.
   */
  isCommitted(json: string | null | undefined): boolean;
  /**
   * The events that bring the server's snapshot to this step with these values (`TypedMachine.plan`, over
   * `fieldsEvent` and `navigation`), or null when there is no server snapshot or it does not read. The snapshot is read
   * as one the server sent.
   */
  plan(
    serverSnapshotJson: string | null | undefined,
    step: Step,
    values: Values,
    options?: Options,
  ): PlanResult<TriggerOf<Spec>> | null;
  /**
   * Brings the server to this step with these values through `controller.syncTo`, which plans from what the server
   * last confirmed and sends one event at a time.
   */
  sync(controller: FormSyncTarget<Spec>, step: Step, values: Values, options?: Options): Promise<SyncOutcome>;
}

/**
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#driving-a-machine-the-client-owns
 */
export function createFormView<
  Spec extends MachineSpec,
  Step extends string,
  Values,
  Options = void,
>(
  machine: TypedMachine<Spec>,
  options: FormViewOptions<Spec, Step, Values, Options>,
): FormView<Spec, Step, Values, Options> {
  const { steps, toContext, fieldsEvent, navigation = [] } = options;

  const inverse = new Map<StateOf<Spec>, Step>();
  for (const [step, state] of Object.entries(steps) as [Step, StateOf<Spec>][]) {
    const claimed = inverse.get(state);
    if (claimed !== undefined) {
      throw new Error(
        `createFormView: steps must be one-to-one, but '${claimed}' and '${step}' both map to '${state}'.`,
      );
    }
    inverse.set(state, step);
  }

  const stateFor = (step: Step): StateOf<Spec> => steps[step];
  const stepFor = (state: StateOf<Spec>): Step => inverse.get(state) as Step;

  // The one cast in here. `snapshotAt` wants the context of one specific state, while `toContext`
  // returns the union across states — it is the caller's projection that ties the two together, and
  // no signature can express that. Confined to this line rather than repeated at every call site.
  const snapshotFor = (step: Step, values: Values, opts?: Options): TypedSnapshot<Spec> =>
    machine.snapshotAt(
      stateFor(step),
      toContext(values, opts) as never,
    );

  const rehydrated = (json: string | null | undefined): TypedSnapshot<Spec> | null => {
    if (!json) return null;
    const result = machine.rehydrate(json);
    return result.result === 'ok' ? result.snapshot : null;
  };

  const fieldsFor = (values: Values, opts?: Options) =>
    fieldsEvent === undefined
      ? null
      : {
          trigger: fieldsEvent.trigger,
          input: fieldsEvent.input ? fieldsEvent.input(values, opts) : toContext(values, opts),
        };

  return {
    stateFor,
    stepFor,
    snapshotFor,

    advance(step, values, trigger, input, opts) {
      const result = machine.core.advance(
        snapshotFor(step, values, opts),
        trigger,
        input,
      );
      return result.outcome === 'transitioned'
        ? { ok: true, step: stepFor(result.snapshot.state as StateOf<Spec>) }
        : { ok: false, reason: result.reason, detail: result.detail };
    },

    can: (step, values, trigger, input, opts) =>
      machine.core.canFire(snapshotFor(step, values, opts), trigger, input),

    serialize: (step, values, opts) =>
      machine.core.serialize(snapshotFor(step, values, opts)),

    resume(json) {
      const snapshot = rehydrated(json);
      if (!snapshot || machine.isCommitted(snapshot.state)) return null;
      return { step: stepFor(snapshot.state), snapshot };
    },

    isCommitted(json) {
      const snapshot = rehydrated(json);
      return snapshot !== null && machine.isCommitted(snapshot.state);
    },

    plan(serverSnapshotJson, step, values, opts) {
      if (!serverSnapshotJson) return null;
      const read = machine.rehydrate(serverSnapshotJson, { fromServer: true });
      if (read.result !== 'ok') return null;
      return machine.plan(read.snapshot, { target: stateFor(step), fields: fieldsFor(values, opts), navigation });
    },

    sync: (controller, step, values, opts) =>
      controller.syncTo(stateFor(step), { fields: fieldsFor(values, opts), navigation }),
  };
}
