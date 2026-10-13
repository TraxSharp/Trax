// How much slower than recorded the landing-page replays play. A recording runs at the sample's real speed, which
// is too quick to follow on a page; every replay reads its clock through replayNow so they all keep one pace.
export const REPLAY_PACE = 2;

/** The replay clock: performance.now(), slowed by REPLAY_PACE. */
export function replayNow(): number {
  return performance.now() / REPLAY_PACE;
}
