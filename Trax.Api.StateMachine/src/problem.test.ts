import { describe, expect, it } from 'vitest';
import { problemFromAdvance, problemFromRehydration } from './problem';
import type { Snapshot } from './types';

const snap: Snapshot = { machine: 'x', version: 1, state: 's', context: {} };

describe('Problem normalizer', () => {
  it('maps a declined advance, preferring detail over reason, and null for a transition', () => {
    expect(problemFromAdvance({ outcome: 'transitioned', snapshot: snap })).toBeNull();
    expect(problemFromAdvance({ outcome: 'rejected', reason: 'guard-failed', detail: 'needs a quarter' })).toEqual({
      kind: 'rejected',
      code: 'guard-failed',
      message: 'needs a quarter',
    });
    expect(problemFromAdvance({ outcome: 'rejected', reason: 'no-transition' })).toEqual({
      kind: 'rejected',
      code: 'no-transition',
      message: 'no-transition',
    });
  });

  it('maps a failed rehydration to a load-error, and null for ok', () => {
    expect(problemFromRehydration({ result: 'ok', snapshot: snap })).toBeNull();
    expect(problemFromRehydration({ result: 'error', code: 'malformed', message: 'bad json' })).toEqual({
      kind: 'load-error',
      code: 'malformed',
      message: 'bad json',
    });
  });
});
