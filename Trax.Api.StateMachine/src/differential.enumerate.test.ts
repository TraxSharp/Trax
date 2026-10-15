import { describe, expect, it } from 'vitest';
import { enumerate, type DifferentialSpec } from './differential';
import { SnapshotMachine } from './machine';
import type { MachineDefinition } from './types';

// A tiny machine with a guarded edge, to prove the `contexts` cross-product probes states x contexts the
// way a complex machine (e.g. a multi-step product wizard) needs — including unreachable (state, context) pairs that
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

// A state seeded more than once: every seed is a start point the search expands, so a context only the second
// seed leads to is still reached. The IR writes one seed as the context itself and several as a list.
describe('differential enumerate: several seeds for one state', () => {
  const def: MachineDefinition<'A' | 'B' | 'C', 'Go'> = {
    id: 'seeds',
    version: 1,
    initialState: 'A',
    createInitialContext: () => ({}),
    states: ['A', 'B', 'C'],
    transitions: [{ from: 'B', trigger: 'Go', to: 'C', guard: (ctx) => ctx.ok === true }],
  };
  const machine = new SnapshotMachine(def);
  const base = { id: 'seeds', version: 1, states: ['A', 'B', 'C'], triggers: ['Go'] };

  const givens = (spec: DifferentialSpec) =>
    new Set(
      enumerate(machine, spec).cases.map(
        (c) => `${c.given.state}:${JSON.stringify(c.given.context)}`,
      ),
    );

  it('expands every seed in a list, including what only a later one reaches', () => {
    const seen = givens({
      ...base,
      differential: { seeds: { B: [{ ok: false }, { ok: true }] } },
    });
    expect(seen.has('B:{"ok":false}')).toBe(true);
    expect(seen.has('B:{"ok":true}')).toBe(true);
    // C is reached only by firing Go from the second seed.
    expect(seen.has('C:{"ok":true}')).toBe(true);
  });

  it('reads a single seed written as the context itself', () => {
    const seen = givens({ ...base, differential: { seeds: { B: { ok: true } } } });
    expect(seen.has('C:{"ok":true}')).toBe(true);
  });
});
