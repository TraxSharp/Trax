// Canonical, stable key derived from an operation's variables, used to index fixtures. The
// capture script and the fixture exchange both derive the key with this function, so a
// request's variables map to the page that was captured for them.
//
// Two normalizations make runtime requests match captured keys:
//   - null/undefined values are dropped. The dashboard passes `status: null`, `trainName: null`
//     etc. for "no filter"; those must match a fixture captured without the variable.
//   - object keys are sorted, so key order never affects the hash.

function normalize(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(normalize);
  if (value && typeof value === "object") {
    const source = value as Record<string, unknown>;
    const out: Record<string, unknown> = {};
    for (const key of Object.keys(source).sort()) {
      const v = source[key];
      if (v === null || v === undefined) continue;
      out[key] = normalize(v);
    }
    return out;
  }
  return value;
}

export function normalizeVariables(vars: Record<string, unknown> | undefined): unknown {
  return normalize(vars ?? {});
}

export function hashVariables(vars: Record<string, unknown> | undefined): string {
  return JSON.stringify(normalizeVariables(vars));
}

/** The key for a first-page / no-variable request. */
export const EMPTY_VARIABLES_HASH = hashVariables({});
