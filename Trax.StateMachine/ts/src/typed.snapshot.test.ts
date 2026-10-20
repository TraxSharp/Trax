import { describe, expect, it } from 'vitest';

import { checkout, checkoutDefinition } from './machines/checkout/checkout';
import { turnstile } from './machines/turnstile/turnstile';

// snapshotAt / isCommitted exist so a client whose UI owns the draft values (a form) never has to
// hardcode what the machine already knows: its id, its schema version, and which states are terminal.

describe('TypedMachine.snapshotAt', () => {
  it('stamps the machine id and version from the definition, not from the caller', () => {
    const snapshot = checkout.snapshotAt('Cart', { currency: 'USD', items: [], receipt: null, total: 0 });

    expect(snapshot.machine).toBe(checkoutDefinition.id);
    expect(snapshot.version).toBe(checkoutDefinition.version);
    expect(snapshot.state).toBe('Cart');
  });

  it('produces a snapshot the engine accepts, so a form can drive the machine without owning it', () => {
    const atCart = checkout.snapshotAt('Cart', { currency: 'USD', items: ['sku-a'], receipt: null, total: 10 });

    expect(checkout.can(atCart, 'Next')).toBe(true);
    expect(checkout.advance(atCart, 'Next')).toMatchObject({ outcome: 'transitioned' });
  });

  it('round-trips through serialize/rehydrate', () => {
    const built = checkout.snapshotAt('Cart', { currency: 'USD', items: [], receipt: null, total: 0 });
    const back = checkout.rehydrate(checkout.serialize(built));

    expect(back).toMatchObject({ result: 'ok', snapshot: built });
  });
});

describe('TypedMachine.isCommitted', () => {
  it('reports the committed states the IR carries', () => {
    expect(checkout.isCommitted('Paid')).toBe(true);
    expect(checkout.isCommitted('Cart')).toBe(false);
  });

  it('is false everywhere on a machine that declares none', () => {
    for (const state of turnstile.states) expect(turnstile.isCommitted(state)).toBe(false);
  });

  it('carries committedStates onto the definition, so the raw engine can see them too', () => {
    expect(checkoutDefinition.committedStates).toEqual(['Paid']);
  });
});
