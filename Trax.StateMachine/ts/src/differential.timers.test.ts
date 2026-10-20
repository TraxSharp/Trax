import { describe, expect, it, vi } from 'vitest';
import { enumerate, type DifferentialSpec } from './differential';
import { SnapshotMachine } from './machine';
import { reminderIr } from './machines/reminder/reminder';
import { machineFromIr } from './rules/irMachine';

// The differential enumerator drives a timer through `applyTimer`, as the C# replay drives it through its timer
// entry point.

describe('differential corpus: entry points', () => {
  it('differential corpus enumerates timer transitions through applyTimer', () => {
    const machine = new SnapshotMachine(machineFromIr(reminderIr));
    const advance = vi.spyOn(machine, 'advance');
    const applyTimer = vi.spyOn(machine, 'applyTimer');

    const corpus = enumerate(machine, reminderIr as DifferentialSpec);

    const timerCases = corpus.cases.filter((c) => c.when.trigger === 'Review.after');
    // The corpus asserts the declared timer and its transition only: no due time and no revision.
    expect(timerCases).toContainEqual({
      given: { machine: 'reminder', version: 1, state: 'Review', context: {} },
      when: { trigger: 'Review.after' },
      expect: {
        outcome: 'transitioned',
        wire: '{"machine":"reminder","version":1,"state":"Expired","context":{}}',
      },
    });
    expect(applyTimer).toHaveBeenCalledWith(expect.anything(), 'Review.after');
    expect(advance.mock.calls.filter(([, trigger]) => trigger === 'Review.after')).toEqual([]);
  });
});
