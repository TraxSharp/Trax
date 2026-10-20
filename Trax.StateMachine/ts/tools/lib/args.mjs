import { realpathSync } from "node:fs";
import { fileURLToPath } from "node:url";

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

/**
 * Whether the module at `moduleUrl` is the script node was started with, rather than a module a test imported.
 * Both paths are resolved first: node's module URL is the real path, and the path it was given may run through
 * a symlink (macOS's /tmp and /var), which would otherwise make the script do nothing and exit 0.
 */
export function invokedDirectly(moduleUrl) {
  const script = process.argv[1];
  if (!script) return false;
  try {
    return realpathSync(script) === realpathSync(fileURLToPath(moduleUrl));
  } catch {
    return false;
  }
}
