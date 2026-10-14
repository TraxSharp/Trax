// ISO 8601 duration <-> { value, unit } for the scheduler settings pickers. The API's TimeSpan
// scalar is ISO 8601 ("PT5S", "PT5M", "PT1H", "P30D"); the UI edits a number + unit instead.

export type DurationUnit = "s" | "m" | "h" | "d";

export const UNIT_SECONDS: Record<DurationUnit, number> = {
  s: 1,
  m: 60,
  h: 3600,
  d: 86400,
};

export const UNIT_LABEL: Record<DurationUnit, string> = {
  s: "seconds",
  m: "minutes",
  h: "hours",
  d: "days",
};

// The scheduler's durations are single-unit; a compound value is normalised to the finest unit
// that divides it evenly, so it round-trips to the same duration.
export function parseDuration(iso: string): { value: string; unit: DurationUnit } {
  const m = /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$/.exec(iso.trim());
  if (!m) return { value: "", unit: "m" };
  const [, d, h, min, s] = m;
  const total =
    (Number(d) || 0) * 86400 +
    (Number(h) || 0) * 3600 +
    (Number(min) || 0) * 60 +
    (Number(s) || 0);
  if (total === 0) return { value: "", unit: "m" };
  for (const u of ["d", "h", "m", "s"] as DurationUnit[])
    if (total % UNIT_SECONDS[u] === 0) return { value: String(total / UNIT_SECONDS[u]), unit: u };
  return { value: String(total), unit: "s" };
}

export function buildDuration(value: string, unit: DurationUnit): string {
  const n = Number(value);
  if (!value || !Number.isFinite(n) || n <= 0) return "";
  return unit === "d" ? `P${n}D` : `PT${n}${unit.toUpperCase()}`;
}
