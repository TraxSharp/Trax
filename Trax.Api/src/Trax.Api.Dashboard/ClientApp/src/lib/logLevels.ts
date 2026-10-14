import type { LogLevelSetting } from "../types";

// The server settings' log-level editor: one level per configured category, saved through
// setLogLevels. Only the categories whose level the operator changed are sent.

/** The levels a category can filter at, by LogLevel name, as config.logLevels reports them. */
export const LOG_LEVEL_NAMES = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"] as const;

/** "Information" -> "INFORMATION", the LogLevel enum setLogLevels takes. */
export function toLogLevelEnum(level: string): string {
  return level.toUpperCase();
}

/** The categories whose edited level differs from the level in force, ready for setLogLevels. */
export function changedLogLevels(
  levels: LogLevelSetting[],
  edits: Record<string, string>,
): { category: string; level: string }[] {
  return levels
    .filter((l) => edits[l.category] != null && edits[l.category] !== l.level)
    .map((l) => ({ category: l.category, level: toLogLevelEnum(edits[l.category]) }));
}

/** What to say when saved levels are not the ones in force, in the Blazor page's words. */
export function notAppliedMessage(categories: string[]): string {
  return (
    "The host sets these categories' levels after the dashboard, so the saved level is not the " +
    `one in force: ${categories.join(", ")}.`
  );
}
