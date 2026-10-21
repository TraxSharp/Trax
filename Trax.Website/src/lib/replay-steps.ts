// Back and Next for the landing page's replays that play one timeline: the places a reader can step to. A replay
// plays itself until the reader steps, and from then on moves only when told.

/**
 * The moments to stop at: events that arrive within `merge` milliseconds of the one before are one step (a request,
 * its answer and the push that follows it), stopped just after the last of them so all of them show.
 */
export function stopsOf(times: number[], merge = 300): number[] {
  const sorted = [...times].filter((t) => Number.isFinite(t)).sort((a, b) => a - b);
  const stops: number[] = [];
  let last = -Infinity;
  for (const t of sorted) {
    if (stops.length && t - last <= merge) stops[stops.length - 1] = t + 1;
    else stops.push(t + 1);
    last = t;
  }
  return stops;
}

/** The first stop after `now`, or the last stop when there is none. */
export function nextStop(stops: number[], now: number): number {
  return stops.find((s) => s > now) ?? stops[stops.length - 1] ?? now;
}

/** The last stop before `now`, or 0 (the start) when there is none. */
export function previousStop(stops: number[], now: number): number {
  for (let i = stops.length - 1; i >= 0; i--) if (stops[i] < now) return stops[i];
  return 0;
}

/** Which step `now` is at, counting from 0 before the first: for the "step 3 / 9" a replay shows. */
export function stepIndex(stops: number[], now: number): number {
  return stops.filter((s) => s <= now).length;
}
