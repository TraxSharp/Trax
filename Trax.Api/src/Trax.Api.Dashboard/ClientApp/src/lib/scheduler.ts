/** What the header says when the scheduler is paused, or null when it runs. */
export function schedulerPaused(
  s: { manifestManagerEnabled: boolean; jobDispatcherEnabled: boolean } | null,
): string | null {
  if (!s) return null;
  if (!s.manifestManagerEnabled && !s.jobDispatcherEnabled) return "Scheduler paused";
  if (!s.manifestManagerEnabled) return "Schedules paused";
  if (!s.jobDispatcherEnabled) return "Dispatch paused";
  return null;
}
