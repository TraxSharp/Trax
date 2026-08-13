/**
 * Minimal `--flag value` / `--boolean` parser for the codegen entrypoints. No dependency, no abbreviations: a
 * token starting with `--` is a flag; the next token is its value unless it is another flag (or absent), in
 * which case the flag is boolean `true`. Repeated flags take the last value.
 */
export function parseFlags(argv) {
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (!token.startsWith("--")) continue;
    const key = token.slice(2);
    const next = argv[i + 1];
    if (next === undefined || next.startsWith("--")) {
      flags[key] = true;
    } else {
      flags[key] = next;
      i++;
    }
  }
  return flags;
}

/** Throw a usage error naming the first missing required flag, so the CLI surfaces a precise message. */
export function requireFlags(flags, required, usage) {
  for (const name of required) {
    const value = flags[name];
    if (value === undefined || value === true)
      throw new Error(`Missing required --${name}.\n${usage}`);
  }
}
