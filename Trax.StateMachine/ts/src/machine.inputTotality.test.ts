import { describe, expect, it } from 'vitest';
import { checkoutCore } from './machines/checkout/checkout';
import { turnstileCore } from './machines/turnstile/turnstile';
import { RefusalCode } from './results';
import { idle, rangeCore } from './rules/testing/rangeMachine';
import type { SnapshotMachine } from './machine';
import type { Snapshot } from './types';

// Totality over trigger input: every input below is bad for every trigger it is sent to, so the engine must
// answer `INVALID_INPUT` for each and never throw.

function nested(depth: number): unknown {
  let node: unknown = 1;
  for (let i = 0; i < depth; i++) node = { from: node };
  return node;
}

const badInputs: [string, unknown][] = [
  ['a string', 's'],
  ['a number', 1],
  ['an array', []],
  ['a boolean', true],
  ['null', null],
  ['an empty object', {}],
  ['an unknown field only', { x: 1 }],
  ['a __proto__ key', JSON.parse('{"__proto__":{"coin":"quarter","receipt":"r","from":1}}')],
  ['a constructor key', { constructor: { prototype: {} } }],
  ['a number beyond double range', JSON.parse('{"from":1e400}')],
  ['a lone surrogate', { note: '\uD800', coin: '\uD800', receipt: '\uDC00' }],
  ['a deeply nested object', nested(1000)],
  ['a long string', 'x'.repeat(1_000_000)],
];

const triggers: [string, SnapshotMachine<string, string>, Snapshot, string][] = [
  ['range Set', rangeCore, idle, 'Set'],
  ['turnstile Coin', turnstileCore, { machine: 'turnstile', version: 1, state: 'Locked', context: {} }, 'Coin'],
  [
    'checkout Pay',
    checkoutCore,
    {
      machine: 'checkout',
      version: 1,
      state: 'Review',
      context: { currency: 'USD', items: ['latte'], receipt: null, total: 5 },
    },
    'Pay',
  ],
];

describe('engine totality: bad trigger input', () => {
  it('a grid of bad inputs never throws, and every verdict is invalid-input', () => {
    const verdicts: string[] = [];
    for (const [name, machine, snapshot, trigger] of triggers)
      for (const [label, input] of badInputs) {
        let reason: string;
        try {
          const result = machine.advance(snapshot, trigger, input);
          reason = result.outcome === 'rejected' ? result.reason : 'transitioned';
        } catch (e) {
          reason = `threw ${String(e)}`;
        }
        verdicts.push(`${name} with ${label}: ${reason}`);
      }

    expect(verdicts).toEqual(
      triggers.flatMap(([name]) =>
        badInputs.map(([label]) => `${name} with ${label}: ${RefusalCode.InvalidInput}`),
      ),
    );
  });
});
