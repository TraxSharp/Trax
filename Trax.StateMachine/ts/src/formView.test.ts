import { describe, expect, it, vi } from 'vitest';

import { createFormView } from './formView';
import type { IrDocument } from './rules/irMachine';
import { typedMachineFromIr } from './typed';
import { checkout } from './machines/checkout/checkout';
import type { CheckoutSpec } from './machines/checkout/checkout.contexts.g';

// A form that owns the values, with the machine as the oracle. The step names are deliberately not the
// machine's, because that mapping is exactly what the view exists to own.
type Step = 'basket' | 'confirm' | 'receipt';
type Values = { skus: string[]; total: number; receipt: string | null };

const view = createFormView<CheckoutSpec, Step, Values, { currency?: string }>(checkout, {
  steps: { basket: 'Cart', confirm: 'Review', receipt: 'Paid' },
  toContext: (values, options = {}) => ({
    currency: options.currency ?? 'USD',
    items: values.skus,
    receipt: values.receipt,
    total: values.total,
  }),
});

const empty: Values = { skus: [], total: 0, receipt: null };
const filled: Values = { skus: ['sku-a'], total: 10, receipt: null };

describe('step mapping', () => {
  it('maps a step to its state', () => {
    expect(view.stateFor('basket')).toBe('Cart');
  });

  it('derives the inverse, so it cannot drift from the map that was written', () => {
    expect(view.stepFor('Cart')).toBe('basket');
    expect(view.stepFor('Paid')).toBe('receipt');
  });

  it('rejects a many-to-one map at construction, where the inverse would be ambiguous', () => {
    expect(() =>
      createFormView<CheckoutSpec, Step, Values, void>(checkout, {
        steps: { basket: 'Cart', confirm: 'Cart', receipt: 'Paid' },
        toContext: () => ({ currency: 'USD', items: [], receipt: null, total: 0 }),
      }),
    ).toThrow(/one-to-one/);
  });
});

describe('snapshotFor', () => {
  it('stamps the machine id and version from the definition, not the caller', () => {
    const snapshot = view.snapshotFor('basket', filled);
    expect(snapshot).toMatchObject({
      machine: checkout.id,
      version: checkout.version,
      state: 'Cart',
    });
  });

  it('passes the options through to the projection', () => {
    expect(view.snapshotFor('basket', filled, { currency: 'GBP' }).context).toMatchObject({
      currency: 'GBP',
    });
  });
});

describe('advance / can', () => {
  it('reports the destination in the UI step vocabulary', () => {
    expect(view.advance('basket', filled, 'Next')).toEqual({ ok: true, step: 'confirm' });
  });

  it('surfaces a declined trigger with its contract reason instead of moving', () => {
    const result = view.advance('basket', empty, 'Next');
    expect(result).toMatchObject({ ok: false, reason: 'GUARD_FAILED' });
  });

  it('can() agrees with advance() without performing the transition', () => {
    expect(view.can('basket', filled, 'Next')).toBe(true);
    expect(view.can('basket', empty, 'Next')).toBe(false);
  });
});

describe('serialize / resume', () => {
  it('round-trips values through the wire and back to a step', () => {
    const json = view.serialize('basket', filled);
    const resumed = view.resume(json);

    expect(resumed?.step).toBe('basket');
    expect(resumed?.snapshot.context).toMatchObject({ items: ['sku-a'], total: 10 });
  });

  it('does not resume a committed run, so it cannot land on its own terminal screen', () => {
    const paid = view.serialize('receipt', { skus: ['sku-a'], total: 10, receipt: 'r-1' });

    expect(view.resume(paid)).toBeNull();
    expect(view.isCommitted(paid)).toBe(true);
  });

  it('tells "nothing stored" apart from "stored and committed"', () => {
    expect(view.resume(null)).toBeNull();
    expect(view.isCommitted(null)).toBe(false);
    expect(view.isCommitted('not json')).toBe(false);
  });

  it('returns null rather than throwing on a snapshot that fails to rehydrate', () => {
    expect(view.resume('{"machine":"checkout","version":99,"state":"Cart","context":{}}')).toBeNull();
  });
});

// A sign-up wizard of three steps whose form saves later: Update writes the name in every step, Next needs it, Back
// returns, Skip rests on a custom guard no handler is bound for, and Send runs an effect.
type SignupFields = { name: string | null };
interface SignupSpec {
  states: { Name: SignupFields; Address: SignupFields; Done: SignupFields; Sent: SignupFields };
  triggers: { Update: { name: string }; Next: undefined; Back: undefined; Skip: undefined; Send: undefined };
}
const nameField = { fields: [{ name: 'name', type: 'string', nullable: true, constraints: [] }] };
const update = (from: string) => ({
  from,
  trigger: 'Update',
  to: from,
  reduce: { reduce: 'set', steps: [{ field: 'name', value: { input: 'name' } }] },
});
const signup = typedMachineFromIr<SignupSpec>({
  id: 'signup',
  version: 1,
  initialState: 'Name',
  initialContext: { name: null },
  states: ['Address', 'Done', 'Name', 'Sent'],
  triggers: ['Back', 'Next', 'Send', 'Skip', 'Update'],
  committedStates: ['Sent'],
  context: { Name: nameField, Address: nameField, Done: nameField, Sent: nameField },
  inputs: { Update: { fields: [{ name: 'name', type: 'string', nullable: false, constraints: [] }] } },
  transitions: [
    update('Name'),
    update('Address'),
    update('Done'),
    { from: 'Name', trigger: 'Next', to: 'Address', guard: { rule: 'present', source: 'context', field: 'name' } },
    { from: 'Name', trigger: 'Skip', to: 'Done', guard: { rule: 'custom', name: 'vip' } },
    { from: 'Address', trigger: 'Next', to: 'Done' },
    { from: 'Address', trigger: 'Back', to: 'Name' },
    { from: 'Done', trigger: 'Back', to: 'Address' },
    { from: 'Done', trigger: 'Send', to: 'Sent', effect: { keyPrefix: 'signup:send' } },
  ],
} as unknown as IrDocument);

type SignupStep = 'name' | 'address' | 'done' | 'sent';
const signupOptions = {
  steps: { name: 'Name', address: 'Address', done: 'Done', sent: 'Sent' } as const,
  toContext: (values: { name: string }) => ({ name: values.name }),
  fieldsEvent: { trigger: 'Update' as const },
  navigation: ['Next', 'Back'] as const,
};
const signupView = createFormView<SignupSpec, SignupStep, { name: string }>(signup, signupOptions);
const server = (state: string, name: string | null) => JSON.stringify({ machine: 'signup', version: 1, state, context: { name } });

describe('plan', () => {
  it('plans the fields, then each step, from the first step to two steps on', () => {
    expect(signupView.plan(server('Name', null), 'done', { name: 'Ada' })).toEqual({
      result: 'plan',
      events: [{ trigger: 'Update', input: { name: 'Ada' } }, { trigger: 'Next' }, { trigger: 'Next' }],
    });
  });

  it('plans nothing when the server already holds that step and those values', () => {
    expect(signupView.plan(server('Done', 'Ada'), 'done', { name: 'Ada' })).toEqual({ result: 'plan', events: [] });
  });

  it('answers unreachable when no event reaches the step', () => {
    expect(signupView.plan(server('Done', 'Ada'), 'sent', { name: 'Ada' })).toEqual({ result: 'unreachable' });
  });

  it('answers undetermined across an edge on a custom guard', () => {
    const skipping = createFormView<SignupSpec, SignupStep, { name: string }>(signup, {
      ...signupOptions,
      navigation: ['Skip'],
    });
    expect(skipping.plan(server('Name', null), 'done', { name: 'Ada' })).toEqual({
      result: 'undetermined',
      trigger: 'Skip',
      events: [],
    });
  });

  it('takes the fields input from its own projection when given one', () => {
    const shouting = createFormView<SignupSpec, SignupStep, { name: string }>(signup, {
      ...signupOptions,
      fieldsEvent: { trigger: 'Update', input: (values) => ({ name: values.name.toUpperCase() }) },
    });
    expect(shouting.plan(server('Done', 'Ada'), 'done', { name: 'Ada' })).toEqual({
      result: 'plan',
      events: [{ trigger: 'Update', input: { name: 'ADA' } }],
    });
  });

  it('answers null when there is no server snapshot, or it does not read', () => {
    expect(signupView.plan(null, 'done', { name: 'Ada' })).toBeNull();
    expect(signupView.plan('not json', 'done', { name: 'Ada' })).toBeNull();
  });
});

describe('sync', () => {
  it('syncs a controller to the step\'s state with the fields event and the navigation', async () => {
    const syncTo = vi.fn().mockResolvedValue({ outcome: 'superseded' });

    expect(await signupView.sync({ syncTo }, 'address', { name: 'Ada' })).toEqual({ outcome: 'superseded' });
    expect(syncTo).toHaveBeenCalledWith('Address', {
      fields: { trigger: 'Update', input: { name: 'Ada' } },
      navigation: ['Next', 'Back'],
    });
  });
});
