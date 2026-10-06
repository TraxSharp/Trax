import { describe, expect, it } from 'vitest';
import type { Snapshot } from '../../types';
import { turnstileCore } from './turnstile';

const STATES = ['Locked', 'Unlocked', 'Broken', ''];
const TRIGGERS = ['Coin', 'Push', 'Zap', ''];
const INPUTS: unknown[] = [undefined, null, 42, {}, { coin: 'quarter' }, { coin: 123 }, [1, 2, 3]];

describe('turnstile totality', () => {
  it('advance never throws over the full state x trigger x input grid', () => {
    for (const state of STATES) {
      for (const trigger of TRIGGERS) {
        for (const input of INPUTS) {
          const snapshot: Snapshot = { machine: 'turnstile', version: 1, state, context: {} };
          const result = turnstileCore.advance(snapshot, trigger, input);
          expect(result.outcome === 'transitioned' || result.outcome === 'rejected').toBe(true);
        }
      }
    }
  });

  const GARBAGE = [
    '',
    'null',
    'true',
    '42',
    '"a string"',
    '[1,2,3]',
    '{',
    '{"machine":123}',
    '{"machine":"turnstile","version":1,"state":"Locked","context":42}',
    '{"machine":"turnstile","version":null,"state":"Locked","context":{}}',
  ];

  it('rehydrate never throws over arbitrary garbage', () => {
    for (const json of GARBAGE) {
      const result = turnstileCore.rehydrate(json);
      expect(result.result).toBe('error');
    }
  });

  it('accepts an integral version written as 1, 1.0 or 1e0 and normalizes it to 1', () => {
    for (const version of ['1', '1.0', '1e0']) {
      const result = turnstileCore.rehydrate(`{"machine":"turnstile","version":${version},"state":"Locked","context":{}}`);
      expect(result.result).toBe('ok');
      if (result.result === 'ok') expect(result.snapshot.version).toBe(1);
    }
  });

  it('treats a non-integral or out-of-range version as malformed', () => {
    for (const version of ['1.5', '1e400']) {
      const result = turnstileCore.rehydrate(`{"machine":"turnstile","version":${version},"state":"Locked","context":{}}`);
      expect(result.result).toBe('error');
      if (result.result === 'error') expect(result.code).toBe('malformed');
    }
  });
});
