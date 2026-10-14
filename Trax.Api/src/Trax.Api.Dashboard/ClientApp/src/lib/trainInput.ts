import type { InputPropertySchema } from "../types";

// Building a train's input JSON from the per-property form the Queue and Run dialogs render from
// trains.inputSchema. Names come from the schema as the API's reader spells them, and an enum
// property's value is one of its enumValues as given, so the JSON is what the queue and run paths
// deserialize.

/** Whether a schema property is a boolean (rendered as a checkbox). */
export function isBooleanProperty(p: InputPropertySchema): boolean {
  return /^(system\.)?bool(ean)?\??$/i.test(p.typeName);
}

/** Whether a schema property is an enum with known members (rendered as a dropdown). */
export function isEnumProperty(p: InputPropertySchema): boolean {
  return (p.enumValues?.length ?? 0) > 0;
}

/**
 * Assemble the filled fields into a JSON object, coercing each value by its declared type. Empty
 * fields are omitted. Returns "" when nothing is filled (the train gets Unit or its defaults).
 */
export function buildInputJson(
  schema: InputPropertySchema[],
  fields: Record<string, string>,
): string {
  const obj: Record<string, unknown> = {};
  for (const p of schema) {
    const v = fields[p.name];
    if (v == null || v === "") continue;
    obj[p.name] = isEnumProperty(p) ? v : coerce(p.typeName, v);
  }
  return Object.keys(obj).length ? JSON.stringify(obj) : "";
}

export function coerce(typeName: string, value: string): unknown {
  const t = typeName.toLowerCase();
  if (/int|long|short|byte|double|single|float|decimal/.test(t)) {
    const n = Number(value);
    return Number.isFinite(n) ? n : value;
  }
  if (t.includes("bool")) return value.toLowerCase() === "true";
  return value;
}
