import { describe, expect, it } from 'vitest';
import { ingest, ingestCore } from './machines/ingest/ingest';
import { reminderAt, reminderCore } from './machines/reminder/reminder';
import { RefusalCode } from './results';

// One engine contract, the same in both engines: `advance` takes only the triggers a caller sends. An invoked
// train's outcome goes through `applyOutcome` and a timer through `applyTimer`.

describe('the engine contract', () => {
  it('advance refuses outcome and timer triggers', () => {
    const fetching = ingest.snapshotAt('Fetching', { source: 's3://bucket/a' });
    const output = { fingerprint: 'sha256:abc', unsure: false };
    const review = reminderAt('Review');

    expect(ingestCore.advance(fetching, 'Fetching.done', output)).toMatchObject({
      outcome: 'rejected',
      reason: RefusalCode.OutcomeBound,
    });
    expect(reminderCore.advance(review, 'Review.after')).toMatchObject({
      outcome: 'rejected',
      reason: RefusalCode.TimerBound,
    });

    expect(ingestCore.applyOutcome(fetching, 'Fetching.done', output)).toMatchObject({
      outcome: 'transitioned',
      snapshot: { state: 'Fetched' },
    });
    expect(reminderCore.applyTimer(review, 'Review.after')).toMatchObject({
      outcome: 'transitioned',
      snapshot: { state: 'Expired' },
    });
  });
});
