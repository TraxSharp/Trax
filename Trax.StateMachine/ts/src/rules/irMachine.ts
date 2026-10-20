// Builds a runnable TS machine from the IR (the formalized machine.json the C# source exports). Guards,
// reducers, and per-state validators are constructed from the IR's declarative data via the interpreters, so
// the TS runtime runs exactly the rules the C# source authored, no hand-written twin.

import type {
  ContextValidator,
  InputValidator,
  MachineDefinition,
  MachineTimer,
  TransitionDefinition,
} from "../types";
import { requireReadableIrFormat } from "./irFormat";
import {
  applyReduction,
  evaluateRule,
  validateInput,
  validateSchema,
  type ContextSchema,
  type CustomGuards,
  type CustomReducers,
  type Reduction,
  type Rule,
} from "./interpreter";

export interface IrTransition {
  from: string;
  trigger: string;
  to: string;
  guard?: Rule;
  guardMessage?: string;
  reduce?: Reduction;
  effect?: { keyPrefix: string };
}

/** One edge an invoked train's outcome can take; `guard` reads the train's output as the input. */
export interface IrOutcomeEdge {
  to: string;
  guard?: Rule;
  reduce?: Reduction;
}

/**
 * An outcome trigger of a state that invokes a train, keyed in `IrDocument.outcomes` by its name
 * (`<state>.done`, `.failed`, `.cancelled`). `edges` are in declaration order, which is the order they are
 * tried in. The train is not named: the twin never starts it.
 */
export interface IrOutcome {
  state: string;
  outcome: "done" | "failed" | "cancelled";
  edges: IrOutcomeEdge[];
  /** The schema of the train's output, the success outcome's input. Present on the `done` outcome only. */
  output?: ContextSchema;
}

/**
 * A state's timer: from `state`, the trigger `trigger` moves to `target` once the duration has passed. The
 * duration is fixed (`after`, an ISO-8601 duration) or read from the context field `field.name` at entry,
 * bounded by `field.min` and `field.max` (ISO-8601 durations). A trigger kind of its own: a client never
 * sends it, and the server fires it.
 */
export interface IrTimer {
  state: string;
  trigger: string;
  target: string;
  after?: string;
  field?: { name: string; min: string; max: string };
  /**
   * The rules the timer acts under: the union of the rules of everything that enters its state, as the census writes
   * them (`Requires(Editor)`). For the census and the startup comparison; the twin does not read it.
   */
  rules?: string[];
}

export interface IrDocument {
  // The IR format a generated twin was generated for (irFormat.ts). The C# export does not write it, so it is part of
  // neither the committed ir.json nor its hash; machineFromIr refuses a format this engine cannot read.
  irFormat?: number;
  id: string;
  version: number;
  initialState: string;
  initialContext?: Record<string, unknown>;
  states: string[];
  triggers: string[];
  committedStates: string[];
  context: Record<string, ContextSchema>;
  inputs: Record<string, ContextSchema>;
  invariants?: Record<string, Rule>;
  transitions: IrTransition[];
  // The outcome triggers of states that invoke a train, a trigger kind of their own. Absent when the machine
  // invokes nothing. The success outcome's input schema (the train's output) is its `output`; `inputs` holds only
  // what a client may send.
  outcomes?: Record<string, IrOutcome>;
  // The timers of timed states, a server-only trigger kind of their own. Absent when the machine declares none.
  timers?: IrTimer[];
  // Differential fuzzing inputs (test-only), authored in C# via .Differential(...) and exported here so the
  // cross-language differential harness enumerates off this IR instead of a hand-written machine.json. Absent
  // on machines with no cross-language differential. Structurally the same block DifferentialSpec carries, so
  // an IrDocument is a valid DifferentialSpec for enumerate(). Stripped from the generated runtime machine.
  differential?: {
    samples?: Record<string, unknown[]>;
    // A state's seed context, or a list of them when the state is seeded more than once.
    seeds?: Record<string, Record<string, unknown> | Record<string, unknown>[]>;
    contexts?: Record<string, unknown>[];
  };
}

/**
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#the-engine-and-the-typed-view
 */
export function machineFromIr(
  ir: IrDocument,
  customGuards: CustomGuards = {},
  customReducers: CustomReducers = {},
): MachineDefinition<string, string> {
  requireReadableIrFormat(ir.irFormat);

  // The machine's actual initial context (what StartsAt built) if the IR carries it, else derived from the
  // initial state's schema. The IR value is authoritative: it reproduces a constrained default (an enum) that
  // the schema alone would default to "" and reject.
  const initialSchema = ir.context[ir.initialState] ?? { fields: [] };
  const createInitialContext = (): Record<string, unknown> =>
    ir.initialContext ? { ...ir.initialContext } : defaultsFor(initialSchema);

  const edge = (
    from: string,
    trigger: string,
    to: string,
    guard: Rule | undefined,
    reduce: Reduction | undefined,
    guardMessage?: string,
  ): TransitionDefinition<string, string> => ({
    from,
    trigger,
    to,
    guard: guard
      ? (ctx, input, options) =>
          evaluateRule(guard, ctx, input, customGuards, options)
      : undefined,
    guardMessage,
    reduce: reduce
      ? (ctx, input) =>
          applyReduction(
            reduce,
            ctx,
            input,
            createInitialContext(),
            customReducers,
          )
      : undefined,
  });

  const transitions: TransitionDefinition<string, string>[] =
    ir.transitions.map((t) =>
      edge(t.from, t.trigger, t.to, t.guard, t.reduce, t.guardMessage),
    );

  // An outcome is an event the twin applies like any trigger, through its own edges in declaration order (the
  // first whose guard accepts the train's output wins, as on the server). Its name has a dot, which no C#
  // trigger can, so it never collides with one.
  const outcomeTriggers = Object.keys(ir.outcomes ?? {}).sort();
  for (const name of outcomeTriggers) {
    const outcome = (ir.outcomes as Record<string, IrOutcome>)[name];
    for (const e of outcome.edges)
      transitions.push(edge(outcome.state, name, e.to, e.guard, e.reduce));
  }

  // Every trigger's input is checked against one schema before any guard reads it, as on the server: the record
  // it declares, else nothing (only no input or `{}`), except that an effect-bound trigger takes the receipt the
  // server's effect runner hands it. A success outcome's input is the train's output; a failure or a cancel
  // carries none.
  const inputValidators: Partial<Record<string, InputValidator>> = {};
  const effectBound = new Set(
    ir.transitions.filter((t) => t.effect).map((t) => t.trigger),
  );
  for (const trigger of ir.triggers) {
    const schema =
      ir.inputs[trigger] ??
      (effectBound.has(trigger) ? receiptInput : emptyInput);
    inputValidators[trigger] = (input) => validateInput(schema, input);
  }
  for (const name of outcomeTriggers) {
    const outcome = (ir.outcomes as Record<string, IrOutcome>)[name];
    const schema =
      outcome.outcome === "done" ? (outcome.output ?? emptyInput) : emptyInput;
    inputValidators[name] = (input) => validateInput(schema, input);
  }

  const contextValidators: Partial<Record<string, ContextValidator>> = {};
  const validatedStates = new Set([
    ...Object.keys(ir.context),
    ...Object.keys(ir.invariants ?? {}),
  ]);
  for (const state of validatedStates) {
    const schema = ir.context[state];
    const invariant = ir.invariants?.[state];
    // A snapshot the server sent was checked there, custom rules included, so on that read a custom rule this twin
    // has no handler for counts as passed. Everywhere else (a predicted transition, a stored draft) it is a reject:
    // the twin cannot tell that it holds, so it does not predict past it and waits for the server.
    const serverGuards = invariant
      ? trustUnhandled(invariant, customGuards)
      : customGuards;
    contextValidators[state] = (ctx, options) => {
      if (schema) {
        const error = validateSchema(schema, ctx);
        if (error) return error;
      }
      // The per-state .Requires policy, evaluated after the shape with the bound custom handlers, as the C# validator
      // evaluates it.
      const guards = options?.fromServer ? serverGuards : customGuards;
      if (invariant && !evaluateRule(invariant, ctx, null, guards))
        return "A state requirement was not satisfied.";
      return null;
    };
  }

  // A timer is a trigger kind of its own: it is not an edge a caller fires, so it stays out of `transitions`, and
  // only `applyTimer` takes it. The rules it acts under are the server's concern.
  const timers: Partial<Record<string, MachineTimer>> = {};
  for (const t of ir.timers ?? [])
    timers[t.state] = {
      trigger: t.trigger,
      target: t.target,
      ...(t.after !== undefined ? { after: t.after } : {}),
      ...(t.field !== undefined ? { field: { ...t.field } } : {}),
    };

  return {
    id: ir.id,
    version: ir.version,
    initialState: ir.initialState,
    createInitialContext,
    states: ir.states,
    transitions,
    committedStates: ir.committedStates,
    outcomeTriggers,
    effectTriggers: [...effectBound].sort(),
    timers,
    inputValidators,
    contextValidators,
  };
}

const emptyInput: ContextSchema = { fields: [] };

// The handlers `rule` is evaluated with on a snapshot the server sent: the bound ones, and a passing one for every
// custom rule it names that has none.
function trustUnhandled(rule: Rule, bound: CustomGuards): CustomGuards {
  const guards: CustomGuards = { ...bound };
  const visit = (r: Rule): void => {
    if (r.rule === "custom") {
      if (!Object.prototype.hasOwnProperty.call(guards, r.name)) guards[r.name] = () => true;
    } else if (r.rule === "all" || r.rule === "any") r.rules.forEach(visit);
  };
  visit(rule);
  return guards;
}

// What an effect-bound trigger that declares no record takes: the receipt the effect runner hands it, or nothing.
// The C# builder derives the same schema for the same edge.
const receiptInput: ContextSchema = {
  fields: [
    { name: "receipt", type: "string", nullable: true, constraints: [] },
  ],
};

// The initial context derived from the initial state's schema: array -> [], number -> 0, string -> "",
// boolean -> false, object -> {}, nullable -> null. This is the string-free StartsAt the C# side also derives.
function defaultsFor(schema: ContextSchema): Record<string, unknown> {
  const ctx: Record<string, unknown> = {};
  for (const field of schema.fields) {
    if (field.nullable) {
      ctx[field.name] = null;
      continue;
    }
    switch (field.type) {
      case "array":
        ctx[field.name] = [];
        break;
      case "number":
      case "integer":
        ctx[field.name] = 0;
        break;
      case "boolean":
        ctx[field.name] = false;
        break;
      case "string":
        ctx[field.name] = field.enum?.[0] ?? "";
        break;
      case "object":
        ctx[field.name] = {};
        break;
    }
  }
  return ctx;
}
