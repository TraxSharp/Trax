import { describe, expect, it } from 'vitest';
import { reminderAt, reminderCore, waitCore } from './reminder';

// A timer is a trigger kind of its own: the server fires it, a client never sends it, and the twin can say
// which timer a state has but not when it is due (it holds no entry time).

const review = reminderAt('Review');

describe('reminder timers in the twin', () => {
  it('can and available exclude timer triggers', () => {
    // The timer is live: applyTimer moves Review to Expired.
    expect(reminderCore.applyTimer(review, 'Review.after')).toMatchObject({
      outcome: 'transitioned',
      snapshot: { state: 'Expired' },
    });
    // It is still never offered to, or fireable by, a caller.
    expect(reminderCore.canFire(review, 'Review.after')).toBe(false);
    expect(reminderCore.availableTriggers(review)).toEqual(['Approve', 'Extend']);
  });

  it('timer(snapshot) reports trigger, target and declared duration', () => {
    expect(reminderCore.timer(review)).toEqual({ trigger: 'Review.after', target: 'Expired', after: 'PT10M' });
    expect(waitCore.timer({ machine: 'wait', version: 1, state: 'Waiting', context: { patience: 'PT5M' } })).toEqual({
      trigger: 'Waiting.after',
      target: 'TimedOut',
      field: { name: 'patience', min: 'PT1M', max: 'PT1H' },
    });
    // No timer on Idle, and no due time anywhere: the server sends that beside the snapshot.
    expect(reminderCore.timer(reminderAt('Idle'))).toBeNull();
  });
});
