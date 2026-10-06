import { describe, expect, it } from 'vitest';
import { enumerate, type DifferentialSpec } from './differential';
import { SnapshotMachine } from './machine';
import type { MachineDefinition } from './types';

// A tiny machine with a guarded edge, to prove the `contexts` cross-product probes states x contexts the
// way a complex machine (e.g. write-to-congress) needs — including unreachable (state, context) pairs that
// BFS + seeds never visit, which is where invalid-context / guard-failed divergences hide.
describe('differential enumerate: contexts cross-product', () => {
  const def: MachineDefinition<'A' | 'B', 'Go'> = {
    id: 'probe',
    version: 1,
    initialState: 'A',
    createInitialContext: () => ({ ok: false }),
    states: ['A', 'B'],
    transitions: [{ from: 'A', trigger: 'Go', to: 'B', guard: (ctx) => ctx.ok === true }],
    contextValidators: { B: (ctx) => (ctx.ok === true ? null : 'B requires ok') },
  };
  const machine = new SnapshotMachine(def);

  const spec: DifferentialSpec = {
    id: 'probe',
    version: 1,
    states: ['A', 'B'],
    triggers: ['Go'],
    differential: { contexts: [{ ok: true }, { ok: false }] },
  };

  it('crosses each declared context with every state, including unreachable pairs', () => {
    const corpus = enumerate(machine, spec);
    const givens = new Set(
      corpus.cases.map((c) => `${c.given.state}:${JSON.stringify(c.given.context)}`),
    );
    for (const g of ['A:{"ok":true}', 'A:{"ok":false}', 'B:{"ok":true}', 'B:{"ok":false}']) {
      expect(givens.has(g)).toBe(true);
    }
  });

  it('catches the guard branch at A (Go passes with ok, fails without)', () => {
    const corpus = enumerate(machine, spec);
    const outcomeAt = (ok: boolean) =>
      corpus.cases.find(
        (c) =>
          c.given.state === 'A' &&
          (c.given.context as { ok: boolean }).ok === ok &&
          c.when.trigger === 'Go',
      )?.expect;
    expect(outcomeAt(true)).toEqual({ outcome: 'transitioned', wire: expect.any(String) });
    expect(outcomeAt(false)).toEqual({ outcome: 'rejected', reason: 'guard-failed' });
  });
});
