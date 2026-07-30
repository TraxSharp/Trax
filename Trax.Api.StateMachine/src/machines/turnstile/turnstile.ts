import { SnapshotMachine } from '../../machine';
import { TypedMachine, type MachineSpec } from '../../typed';
import type { Guard, MachineDefinition, Reduce, TransitionDefinition } from '../../types';
import { turnstileStructure, type TurnstileState, type TurnstileTrigger } from './turnstile.g';

export type { TurnstileState, TurnstileTrigger };

const ACCEPTED_COINS = new Set(['quarter', 'dollar']);

const coin = (input: unknown): string | undefined => {
  if (input && typeof input === 'object' && 'coin' in input) {
    const c = (input as { coin?: unknown }).coin;
    return typeof c === 'string' ? c : undefined;
  }
  return undefined;
};

// Behavior, bound to the generated edges by NAME. The snapshot carries structure + data, never logic.
const guards: Record<string, Guard> = {
  acceptedCoin: (_ctx, input) => ACCEPTED_COINS.has(coin(input) ?? ''),
};

const reducers: Record<string, Reduce> = {
  recordCoin: (_ctx, input) => ({ paidWith: coin(input) }),
  clear: () => ({}),
};

const transitions: TransitionDefinition<TurnstileState, TurnstileTrigger>[] = turnstileStructure.edges.map((e) => ({
  from: e.from,
  trigger: e.trigger,
  to: e.to,
  guard: e.guard ? guards[e.guard] : undefined,
  guardMessage: e.guardMessage,
  reduce: e.reduce ? reducers[e.reduce] : undefined,
}));

/**
 * The turnstile proof machine — the behavioral twin of the C# TestTurnstile and the shared fixtures.
 * Structure comes from the generated {@link turnstileStructure}; guards/reducers are hand-written here
 * and bound by name; context policy (the validators) is hand-written per state.
 */
export const turnstileDefinition: MachineDefinition<TurnstileState, TurnstileTrigger> = {
  id: turnstileStructure.id,
  version: turnstileStructure.version,
  initialState: turnstileStructure.initialState,
  createInitialContext: () => ({}),
  states: turnstileStructure.states,
  transitions,
  contextValidators: {
    Locked: (ctx) => (Object.keys(ctx).length === 0 ? null : 'Locked carries no context.'),
    Unlocked: (ctx) =>
      typeof ctx.paidWith === 'string' && ctx.paidWith.length > 0 ? null : 'Unlocked requires a non-empty paidWith.',
  },
};

export const turnstileCore = new SnapshotMachine(turnstileDefinition);

/** The compile-time surface a UI imports: `state`/`context` discriminated, `send`/`can` input-checked. */
export interface TurnstileSpec extends MachineSpec {
  states: {
    Locked: Record<string, never>;
    Unlocked: { paidWith: string };
  };
  triggers: {
    Coin: { coin: string };
    Push: undefined;
  };
}

export const turnstile = new TypedMachine<TurnstileSpec>(turnstileCore);
