import { SnapshotMachine } from '../../machine';
import { TypedMachine, type MachineSpec } from '../../typed';
import type { Guard, MachineDefinition, Reduce, TransitionDefinition } from '../../types';
import { checkoutStructure, type CheckoutState, type CheckoutTrigger } from './checkout.g';

export type { CheckoutState, CheckoutTrigger };

const itemsOf = (ctx: Record<string, unknown>): unknown[] => (Array.isArray(ctx.items) ? ctx.items : []);
const totalOf = (ctx: Record<string, unknown>): number => (typeof ctx.total === 'number' ? ctx.total : 0);
const receiptEmpty = (ctx: Record<string, unknown>): boolean => ctx.receipt === null || ctx.receipt === undefined;
const receiptPresent = (ctx: Record<string, unknown>): boolean => typeof ctx.receipt === 'string' && ctx.receipt.length > 0;
const receiptInput = (input: unknown): string | undefined =>
  input && typeof input === 'object' && typeof (input as { receipt?: unknown }).receipt === 'string'
    ? (input as { receipt: string }).receipt
    : undefined;

const guards: Record<string, Guard> = {
  hasItems: (ctx) => itemsOf(ctx).length > 0,
  payable: (ctx, input) => itemsOf(ctx).length > 0 && totalOf(ctx) > 0 && receiptInput(input) !== undefined,
};

const reducers: Record<string, Reduce> = {
  applyReceipt: (ctx, input) => ({ ...ctx, receipt: receiptInput(input) }),
  freshCart: () => ({ currency: 'USD', items: [], receipt: null, total: 0 }),
};

const transitions: TransitionDefinition<CheckoutState, CheckoutTrigger>[] = checkoutStructure.edges.map((e) => ({
  from: e.from,
  trigger: e.trigger,
  to: e.to,
  guard: e.guard ? guards[e.guard] : undefined,
  guardMessage: e.guardMessage,
  reduce: e.reduce ? reducers[e.reduce] : undefined,
}));

/** The multi-step, effectful demo machine: structure generated, behavior/policy hand-written by name. */
export const checkoutDefinition: MachineDefinition<CheckoutState, CheckoutTrigger> = {
  id: checkoutStructure.id,
  version: checkoutStructure.version,
  initialState: checkoutStructure.initialState,
  createInitialContext: () => ({ currency: 'USD', items: [], receipt: null, total: 0 }),
  states: checkoutStructure.states,
  transitions,
  contextValidators: {
    Cart: (ctx) =>
      Array.isArray(ctx.items) && typeof ctx.total === 'number' && receiptEmpty(ctx)
        ? null
        : 'Cart: items[], a numeric total, and no receipt.',
    Review: (ctx) =>
      itemsOf(ctx).length > 0 && totalOf(ctx) > 0 && receiptEmpty(ctx)
        ? null
        : 'Review: non-empty items, a positive total, and no receipt.',
    Paid: (ctx) => (itemsOf(ctx).length > 0 && receiptPresent(ctx) ? null : 'Paid: non-empty items and a receipt.'),
  },
};

export const checkoutCore = new SnapshotMachine(checkoutDefinition);

export interface CheckoutSpec extends MachineSpec {
  states: {
    Cart: { currency: string; items: string[]; receipt: null; total: number };
    Review: { currency: string; items: string[]; receipt: null; total: number };
    Paid: { currency: string; items: string[]; receipt: string; total: number };
  };
  triggers: {
    Next: undefined;
    Back: undefined;
    Pay: { receipt: string };
    Restart: undefined;
  };
}

export const checkout = new TypedMachine<CheckoutSpec>(checkoutCore);
