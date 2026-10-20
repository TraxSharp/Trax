import fs from 'node:fs';
import { describe, expect, it } from 'vitest';
import { structureFile, wireHandoffFile } from '../../fixtures';
import type { Snapshot } from '../../types';
import { checkoutCore } from './checkout';

describe('checkout serialization (multi-key canonicalization — closes the G4 gap)', () => {
  it('matches every wire-handoff sample, sorting context keys regardless of insertion order', () => {
    const handoff = JSON.parse(fs.readFileSync(wireHandoffFile('checkout'), 'utf8')) as {
      samples: { name: string; build: Snapshot; snapshot: string }[];
    };
    for (const sample of handoff.samples) {
      // The build lists context keys out of order; serialize must emit them sorted (currency, items, receipt, total).
      expect(checkoutCore.serialize(sample.build), sample.name).toBe(sample.snapshot);
    }
  });

  it('serialize then rehydrate is identity for a multi-key snapshot', () => {
    const snapshot: Snapshot = {
      machine: 'checkout',
      version: 1,
      state: 'Paid',
      context: { currency: 'USD', items: ['latte', 'muffin'], receipt: 'txn-1', total: 42 },
    };
    expect(checkoutCore.rehydrate(checkoutCore.serialize(snapshot))).toEqual({ result: 'ok', snapshot });
  });
});

describe('checkout structure golden', () => {
  it('describe() equals the committed structure.json', () => {
    const golden = JSON.parse(fs.readFileSync(structureFile('checkout'), 'utf8')) as Record<string, unknown>;
    delete golden._comment;
    expect(checkoutCore.describe()).toEqual(golden);
  });
});
