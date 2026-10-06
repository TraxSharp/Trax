import { describe, expect, it } from 'vitest';

import { createFormView } from './formView';
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
    expect(result).toMatchObject({ ok: false, reason: 'guard-failed' });
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
