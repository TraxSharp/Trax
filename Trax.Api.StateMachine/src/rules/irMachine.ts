// Builds a runnable TS machine from the IR (the formalized machine.json the C# source exports). Guards,
// reducers, and per-state validators are constructed from the IR's declarative data via the interpreters, so
// the TS runtime runs exactly the rules the C# source authored, no hand-written twin.

import type {
  ContextValidator,
  MachineDefinition,
  TransitionDefinition,
} from "../types";
import {
  applyReduction,
  evaluateRule,
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
  effect?: { type: string | null; keyPrefix: string };
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
 * tried in. `train` is the train's canonical name; the twin never starts it.
 */
export interface IrOutcome {
  state: string;
  outcome: "done" | "failed" | "cancelled";
  train: string;
  edges: IrOutcomeEdge[];
}

export interface IrDocument {
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
  // invokes nothing. The success outcome's input schema (the train's output) is under `inputs`.
  outcomes?: Record<string, IrOutcome>;
  // Differential fuzzing inputs (test-only), authored in C# via .Differential(...) and exported here so the
  // cross-language differential harness enumerates off this IR instead of a hand-written machine.json. Absent
  // on machines with no cross-language differential. Structurally the same block DifferentialSpec carries, so
  // an IrDocument is a valid DifferentialSpec for enumerate(). Stripped from the generated runtime machine.
  differential?: {
    samples?: Record<string, unknown[]>;
    seeds?: Record<string, Record<string, unknown>>;
    contexts?: Record<string, unknown>[];
  };
}

export function machineFromIr(
  ir: IrDocument,
  customGuards: CustomGuards = {},
  customReducers: CustomReducers = {},
): MachineDefinition<string, string> {
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
      ? (ctx, input) => evaluateRule(guard, ctx, input, customGuards)
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

  const contextValidators: Partial<Record<string, ContextValidator>> = {};
  const validatedStates = new Set([
    ...Object.keys(ir.context),
    ...Object.keys(ir.invariants ?? {}),
  ]);
  for (const state of validatedStates) {
    const schema = ir.context[state];
    const invariant = ir.invariants?.[state];
    contextValidators[state] = (ctx) => {
      if (schema) {
        const error = validateSchema(schema, ctx);
        if (error) return error;
      }
      // The per-state .Requires policy, evaluated after the shape. Matches the C# validator (no custom
      // handlers at the validator layer), so a custom rule here is a reject.
      if (invariant && !evaluateRule(invariant, ctx, null))
        return "A state requirement was not satisfied.";
      return null;
    };
  }

  return {
    id: ir.id,
    version: ir.version,
    initialState: ir.initialState,
    createInitialContext,
    states: ir.states,
    transitions,
    committedStates: ir.committedStates,
    outcomeTriggers,
    contextValidators,
  };
}

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
        ctx[field.name] = 0;
        break;
      case "boolean":
        ctx[field.name] = false;
        break;
      case "string":
        ctx[field.name] = "";
        break;
      case "object":
        ctx[field.name] = {};
        break;
    }
  }
  return ctx;
}
