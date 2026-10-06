// Small display helpers shared by the pages added for Blazor parity.

/** The last dotted segment of a type or train FullName ("Trax.Demo.OrderTrain" -> "OrderTrain"). */
export function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}

/** A millisecond duration for people: "850 ms", "12.4 s", "3m 05s", "2h 07m". */
export function formatMs(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return "—";
  if (ms < 1000) return `${Math.round(ms)} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
  const totalSeconds = Math.floor(ms / 1000);
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  return h > 0 ? `${h}h ${String(m).padStart(2, "0")}m` : `${m}m ${String(s).padStart(2, "0")}s`;
}

/** A timestamp in the viewer's locale, or the fallback when there is none. */
export function formatTime(iso: string | null | undefined, fallback = "—"): string {
  return iso ? new Date(iso).toLocaleString() : fallback;
}

/** Pretty-print JSON when it parses; otherwise the text as it came. */
export function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

/** "AWAITING_INTERVENTION" -> "Awaiting intervention". */
export function enumLabel(value: string): string {
  return value
    .toLowerCase()
    .replace(/_/g, " ")
    .replace(/^\w/, (c) => c.toUpperCase());
}
