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

export interface IrDocument {
  id: string;
  version: number;
  initialState: string;
  states: string[];
  triggers: string[];
  committedStates: string[];
  context: Record<string, ContextSchema>;
  inputs: Record<string, ContextSchema>;
  transitions: IrTransition[];
}

export function machineFromIr(
  ir: IrDocument,
  customGuards: CustomGuards = {},
  customReducers: CustomReducers = {},
): MachineDefinition<string, string> {
  const initialSchema = ir.context[ir.initialState] ?? { fields: [] };
  const createInitialContext = () => defaultsFor(initialSchema);

  const transitions: TransitionDefinition<string, string>[] =
    ir.transitions.map((t) => ({
      from: t.from,
      trigger: t.trigger,
      to: t.to,
      guard: t.guard
        ? (ctx, input) =>
            evaluateRule(t.guard as Rule, ctx, input, customGuards)
        : undefined,
      guardMessage: t.guardMessage,
      reduce: t.reduce
        ? (ctx, input) =>
            applyReduction(
              t.reduce as Reduction,
              ctx,
              input,
              createInitialContext(),
              customReducers,
            )
        : undefined,
    }));

  const contextValidators: Partial<Record<string, ContextValidator>> = {};
  for (const [state, schema] of Object.entries(ir.context))
    contextValidators[state] = (ctx) => validateSchema(schema, ctx);

  return {
    id: ir.id,
    version: ir.version,
    initialState: ir.initialState,
    createInitialContext,
    states: ir.states,
    transitions,
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
