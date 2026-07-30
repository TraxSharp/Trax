import { describe, expect, it } from 'vitest';
import { SnapshotMachine } from '../../machine';
import type { MachineDefinition } from '../../types';
import { turnstileCore, turnstileDefinition, type TurnstileState, type TurnstileTrigger } from './turnstile';

const v2Definition: MachineDefinition<TurnstileState, TurnstileTrigger> = {
  ...turnstileDefinition,
  version: 2,
  migrations: {
    1: (state, ctx) => ({ state, context: { ...ctx, migrated: true } }),
  },
};
const turnstileV2 = new SnapshotMachine(v2Definition);

const V1_UNLOCKED = '{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}';

describe('turnstile migration', () => {
  it('migrates an older snapshot forward on rehydrate', () => {
    const result = turnstileV2.rehydrate(V1_UNLOCKED);
    expect(result.result).toBe('ok');
    if (result.result === 'ok') {
      expect(result.snapshot.version).toBe(2);
      expect(result.snapshot.context).toEqual({ paidWith: 'quarter', migrated: true });
    }
  });

  it('rejects a gap in the migration chain as version-mismatch', () => {
    const v3WithGap = new SnapshotMachine<TurnstileState, TurnstileTrigger>({
      ...turnstileDefinition,
      version: 3,
      migrations: { 1: (state, ctx) => ({ state, context: { ...ctx } }) }, // no 2 -> 3
    });
    const result = v3WithGap.rehydrate(V1_UNLOCKED);
    expect(result.result).toBe('error');
    if (result.result === 'error') expect(result.code).toBe('version-mismatch');
  });

  it('rejects a snapshot newer than the definition as version-mismatch', () => {
    const result = turnstileCore.rehydrate('{"machine":"turnstile","version":2,"state":"Locked","context":{}}');
    expect(result.result).toBe('error');
    if (result.result === 'error') expect(result.code).toBe('version-mismatch');
  });
});
