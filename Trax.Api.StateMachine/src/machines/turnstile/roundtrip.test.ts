import { describe, expect, it } from 'vitest';
import { structureFile, wireHandoffFile } from '../../fixtures';
import fs from 'node:fs';
import type { Snapshot } from '../../types';
import { turnstileCore } from './turnstile';

describe('turnstile serialization', () => {
  it('pins the exact wire bytes for Locked and Unlocked', () => {
    expect(turnstileCore.serialize({ machine: 'turnstile', version: 1, state: 'Locked', context: {} })).toBe(
      '{"machine":"turnstile","version":1,"state":"Locked","context":{}}',
    );
    expect(
      turnstileCore.serialize({ machine: 'turnstile', version: 1, state: 'Unlocked', context: { paidWith: 'quarter' } }),
    ).toBe('{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}');
  });

  it('canonicalizes context keys ordinally, recursively, keeping array order', () => {
    const snapshot: Snapshot = {
      machine: 'turnstile',
      version: 1,
      state: 'Unlocked',
      context: { z: 1, a: 2, m: { y: true, b: false }, items: [{ b: 2, a: 1 }, { a: 3 }] },
    };
    expect(turnstileCore.serialize(snapshot)).toBe(
      '{"machine":"turnstile","version":1,"state":"Unlocked","context":{"a":2,"items":[{"a":1,"b":2},{"a":3}],"m":{"b":false,"y":true},"z":1}}',
    );
  });

  it('serialize then rehydrate is identity for a valid snapshot', () => {
    const snapshot: Snapshot = { machine: 'turnstile', version: 1, state: 'Unlocked', context: { paidWith: 'dollar' } };
    const result = turnstileCore.rehydrate(turnstileCore.serialize(snapshot));
    expect(result).toEqual({ result: 'ok', snapshot });
  });

  it('matches every wire-handoff sample (canonical bytes both engines emit)', () => {
    const handoff = JSON.parse(fs.readFileSync(wireHandoffFile('turnstile'), 'utf8')) as {
      samples: { name: string; build: Snapshot; snapshot: string }[];
    };
    for (const sample of handoff.samples) {
      expect(turnstileCore.serialize(sample.build), sample.name).toBe(sample.snapshot);
    }
  });
});

describe('turnstile structure golden', () => {
  it('describe() equals the committed structure.json (the cross-language structure oracle)', () => {
    const golden = JSON.parse(fs.readFileSync(structureFile('turnstile'), 'utf8')) as Record<string, unknown>;
    delete golden._comment;
    expect(turnstileCore.describe()).toEqual(golden);
  });
});
