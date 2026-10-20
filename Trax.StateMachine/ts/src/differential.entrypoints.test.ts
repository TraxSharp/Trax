import fs from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import { enumerate, type DifferentialSpec } from './differential';
import { irFile } from './fixtures';
import { SnapshotMachine } from './machine';
import { machineFromIr, type IrDocument } from './rules/irMachine';

// The differential enumerator drives each trigger through the entry point that owns it: a caller's trigger
// through `advance`, an outcome through `applyOutcome`. The C# replay routes the same way, so both engines
// replay one corpus through one contract.

const outcomeTrigger = /\.(done|failed|cancelled)$/;

describe('differential corpus: entry points', () => {
  it('The_corpus_replays_outcome_cases_through_the_outcome_entry_point', () => {
    const ir = JSON.parse(fs.readFileSync(irFile('ingest'), 'utf8')) as IrDocument;
    const machine = new SnapshotMachine(machineFromIr(ir));
    const advance = vi.spyOn(machine, 'advance');
    const applyOutcome = vi.spyOn(machine, 'applyOutcome');

    const corpus = enumerate(machine, ir as DifferentialSpec);

    expect(corpus.cases.some((c) => outcomeTrigger.test(c.when.trigger))).toBe(true);
    expect(advance.mock.calls.filter(([, trigger]) => outcomeTrigger.test(trigger))).toEqual([]);
    expect(new Set(applyOutcome.mock.calls.map(([, trigger]) => trigger))).toEqual(
      new Set(['Fetching.cancelled', 'Fetching.done', 'Fetching.failed']),
    );
  });
});
