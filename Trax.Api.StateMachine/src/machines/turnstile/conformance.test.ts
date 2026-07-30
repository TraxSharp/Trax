import { describe, expect, it } from 'vitest';
import { advanceDir, loadFixtures, rehydrateDir, rehydrateInput } from '../../fixtures';
import type { Snapshot } from '../../types';
import { turnstileCore } from './turnstile';

interface AdvanceFixture {
  name?: string;
  given: Snapshot;
  when: { trigger: string; input?: unknown };
  expect: { outcome: 'transitioned'; snapshot: Snapshot } | { outcome: 'rejected'; reason: string };
}

interface RehydrateFixture {
  name?: string;
  expect: { result: 'ok'; snapshot: Snapshot } | { result: 'error'; code: string };
}

describe('turnstile advance conformance (shared fixtures)', () => {
  for (const { file, fixture } of loadFixtures(advanceDir('turnstile'))) {
    const f = fixture as unknown as AdvanceFixture;
    it(f.name ?? file, () => {
      const result = turnstileCore.advance(f.given, f.when.trigger, f.when.input);
      if (f.expect.outcome === 'transitioned') {
        expect(result).toEqual({ outcome: 'transitioned', snapshot: f.expect.snapshot });
      } else {
        expect(result.outcome).toBe('rejected');
        if (result.outcome === 'rejected') expect(result.reason).toBe(f.expect.reason);
      }
    });
  }
});

describe('turnstile rehydrate conformance (shared fixtures)', () => {
  for (const { file, fixture } of loadFixtures(rehydrateDir('turnstile'))) {
    const f = fixture as unknown as RehydrateFixture;
    it(f.name ?? file, () => {
      const result = turnstileCore.rehydrate(rehydrateInput(fixture));
      if (f.expect.result === 'ok') {
        expect(result).toEqual({ result: 'ok', snapshot: f.expect.snapshot });
      } else {
        expect(result.result).toBe('error');
        if (result.result === 'error') expect(result.code).toBe(f.expect.code);
      }
    });
  }
});
