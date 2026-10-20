// ISO-8601 durations as a timer reads them: days, hours, minutes and seconds (`P1DT2H30M`, `PT0.5S`), never
// negative, with no years, months or weeks (their length is not fixed), each component at most nine digits and a
// fraction of at most seven. The C# engine parses the same subset (`IsoDuration`), so both accept and refuse the same
// text, and the digits are ASCII only.
const pattern =
  /^P(?!$)(?:([0-9]{1,9})D)?(?:T(?=[0-9])(?:([0-9]{1,9})H)?(?:([0-9]{1,9})M)?(?:([0-9]{1,9})(?:\.([0-9]{1,7}))?S)?)?$/;

// The longest duration .NET's TimeSpan holds, in ticks of 100 ns: a longer one is refused as C# refuses it. Compared as
// an exact integer, since a double cannot tell TimeSpan.MaxValue from one tick past it.
const maxTicks = 9_223_372_036_854_775_807n;

/**
 * The duration `text` holds, in ticks of 100 ns (a .NET TimeSpan's unit), or null when it is not one this subset
 * accepts. Exact, so two durations compare as C# compares them. Never throws.
 */
export function durationTicks(text: unknown): bigint | null {
  if (typeof text !== "string") return null;
  const match = pattern.exec(text);
  if (!match) return null;
  const part = (i: number) => BigInt(match[i] ?? "0");
  const fraction = BigInt((match[5] ?? "").padEnd(7, "0"));
  const ticks =
    (part(1) * 86_400n + part(2) * 3_600n + part(3) * 60n + part(4)) * 10_000_000n + fraction;
  return ticks > maxTicks ? null : ticks;
}

/** The duration `text` holds, in seconds, or null when it is not one this subset accepts. Never throws. */
export function parseDuration(text: unknown): number | null {
  const ticks = durationTicks(text);
  return ticks === null ? null : Number(ticks) / 10_000_000;
}
