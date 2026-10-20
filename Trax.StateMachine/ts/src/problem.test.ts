import { describe, expect, it } from 'vitest';
import { problemFromAdvance, problemFromRehydration } from './problem';
import type { Snapshot } from './types';

const snap: Snapshot = { machine: 'x', version: 1, state: 's', context: {} };

describe('Problem normalizer', () => {
  it('maps a declined advance, preferring detail over reason, and null for a transition', () => {
    expect(problemFromAdvance({ outcome: 'transitioned', snapshot: snap })).toBeNull();
    expect(problemFromAdvance({ outcome: 'rejected', reason: 'GUARD_FAILED', detail: 'needs a quarter' })).toEqual({
      kind: 'rejected',
      code: 'GUARD_FAILED',
      message: 'needs a quarter',
    });
    expect(problemFromAdvance({ outcome: 'rejected', reason: 'NO_TRANSITION' })).toEqual({
      kind: 'rejected',
      code: 'NO_TRANSITION',
      message: 'NO_TRANSITION',
    });
  });

  it('maps a failed rehydration to a load-error, and null for ok', () => {
    expect(problemFromRehydration({ result: 'ok', snapshot: snap })).toBeNull();
    expect(problemFromRehydration({ result: 'error', code: 'MALFORMED', message: 'bad json' })).toEqual({
      kind: 'load-error',
      code: 'MALFORMED',
      message: 'bad json',
    });
  });
});
