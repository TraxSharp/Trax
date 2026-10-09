import type { Step } from "./types";

/** How far along a step's state is. A step is never moved back to a state ranked below the one it holds. */
export const RANK = { IN_PROGRESS: 0, COMPLETED: 1, FAILED: 1, CANCELLED: 1 } as const;

/**
 * An attempt's steps by position, with what arrived merged in. A step the page already holds in a later state
 * keeps it: a junction event and a poll can answer out of order, and the older answer must not roll the step
 * back to running.
 */
export function mergeSteps(known: Readonly<Record<number, Step>>, incoming: readonly Step[]): Record<number, Step> {
  const steps = { ...known };
  for (const step of incoming) {
    const held = steps[step.position];
    if (!held || RANK[held.state] < RANK[step.state]) steps[step.position] = step;
  }
  return steps;
}

/**
 * Numbers the reads of each attempt, so an answer to a read that a later one overtook (a slow poll landing after
 * the final read) is dropped rather than drawn over the newer answer.
 */
export class LatestReads {
  private readonly started = new Map<number, number>();

  /** Starts a read of attempt `id`; pass what it returns to {@link isLatest} once the read answers. */
  begin(id: number): number {
    const read = (this.started.get(id) ?? 0) + 1;
    this.started.set(id, read);
    return read;
  }

  /** Whether `read` is still the newest read of attempt `id`. */
  isLatest(id: number, read: number): boolean {
    return this.started.get(id) === read;
  }
}
