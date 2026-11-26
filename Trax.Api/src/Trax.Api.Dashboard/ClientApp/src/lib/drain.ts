// The drain (operations.config.drain): while it holds, no host dispatches, queues a scheduled run
// or claims a job. A deploy sets it before it replaces the hosts; an operator can set it here.

/** The drain length the console offers first, in minutes: a deploy's default timeout plus its margin. */
export const DEFAULT_DRAIN_MINUTES = 20;

/** The longest drain the API takes, in minutes (24 hours). */
export const MAX_DRAIN_MINUTES = 1440;

/** What the header says while a drain is in force, or null when none is (or before it loads). */
export function drainMark(d: { drained: boolean } | null | undefined): string | null {
  return d?.drained ? "Drained" : null;
}

/**
 * How long to wait before reading a drain again once it lapses at `until`: a second past it, so the
 * host's clock has passed it too, and never longer than a browser timer holds.
 */
export function drainRecheckDelay(until: string, now: number): number {
  const at = Date.parse(until);
  if (Number.isNaN(at)) return 60_000;
  return Math.min(Math.max(0, at - now) + 1000, 2_147_483_647);
}

/**
 * Reads the minutes an operator typed: a whole number from 1 to 1440. Returns the number, or the
 * sentence that says what is wrong with it.
 */
export function parseDrainMinutes(text: string): number | string {
  const t = text.trim();
  if (!/^\d+$/.test(t)) return "Give the drain's length as a whole number of minutes.";
  const n = Number(t);
  if (n < 1 || n > MAX_DRAIN_MINUTES) return `A drain lasts from 1 to ${MAX_DRAIN_MINUTES} minutes.`;
  return n;
}

/** Who set a drain, in words: a deploy, or the operator it names. */
export function drainSetBy(startedBy: string | null | undefined): string {
  if (!startedBy) return "someone";
  return startedBy === "deploy" ? "a deploy" : startedBy;
}
