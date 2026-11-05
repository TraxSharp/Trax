import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

// Expected failures: the cases a testee is known to fail, listed per testee, so a language that is not built out yet
// can run every kit green while what it has not built is written down. A list can only shrink: a listed case that
// passes, fails some other way, or is not in the kit fails the run. See "Expected failures" in PROTOCOL.md.

/** What a testee may be named: the file its list is read from, so no path. */
export const TESTEE_NAME = /^[a-z0-9][a-z0-9-]*$/;

/**
 * The list a testee runs under, from `<dir>/<name>.json`: none without a name, and none when the file is absent, since
 * an absent list excuses nothing. `detail` is the key an entry may name what the case fails with under: `code` in the
 * engine kit, `message` in the client and operations kits.
 */
export function readExpectedFailures(dir, name, detail) {
  if (name === undefined) return { path: null, entries: [] };
  if (!TESTEE_NAME.test(name)) throw new Error(`--name ${JSON.stringify(name)} is not a testee name (${TESTEE_NAME}).`);
  const path = join(dir, `${name}.json`);
  if (!existsSync(path)) return { path, entries: [] };
  return { path, entries: checkEntries(JSON.parse(readFileSync(path, "utf8")), detail, path) };
}

/**
 * The entries of a list, each `{ case | prefix, reason, [detail] }`, or an Error naming the first that is not one: a list
 * that cannot be read excuses nothing, so the run stops rather than guess.
 */
export function checkEntries(entries, detail, where = "the list") {
  if (!Array.isArray(entries)) throw new Error(`${where} is not an array of expected failures.`);
  const keys = new Set(["case", "prefix", "reason", detail]);
  for (const e of entries) {
    const shown = JSON.stringify(e);
    if (e === null || typeof e !== "object" || Array.isArray(e)) throw new Error(`${where}: ${shown} is not an object.`);
    const unknown = Object.keys(e).filter((k) => !keys.has(k));
    if (unknown.length > 0) throw new Error(`${where}: ${shown} has ${unknown.join(", ")}, which an entry does not take.`);
    if ((e.case === undefined) === (e.prefix === undefined))
      throw new Error(`${where}: ${shown} names neither or both of case and prefix; it names one.`);
    for (const key of ["case", "prefix", "reason", detail])
      if (e[key] !== undefined && (typeof e[key] !== "string" || e[key].trim() === ""))
        throw new Error(`${where}: ${shown} has a ${key} that is not a non-empty string.`);
    if (e.reason === undefined) throw new Error(`${where}: ${shown} gives no reason.`);
  }
  return entries;
}

const label = (e) => (e.case !== undefined ? `the entry for ${e.case}` : `the entry for the prefix ${e.prefix}`);

/**
 * Judges a run against a list. `names` is every case of the kit, run or not, so an entry that names none is caught under
 * `--only` too. `detailOf(failure)` is what a failure fails with (a code, or a message); `fits(listed, got)` whether it
 * is what the entry names. Each case that ran is settled once, through `settle`.
 */
export function judge(entries, names, { detail, detailOf, fits }) {
  const problems = [];
  const entryOf = new Map();
  for (const e of entries) {
    const covered = names.filter((n) => (e.case !== undefined ? n === e.case : n.startsWith(e.prefix)));
    if (covered.length === 0) problems.push(`${label(e)} names no case of the kit; remove it`);
    for (const n of covered)
      if (entryOf.has(n)) problems.push(`${n} is listed twice, by ${label(entryOf.get(n))} and ${label(e)}`);
      else entryOf.set(n, e);
  }
  let expected = 0;
  return {
    problems,
    get expected() {
      return expected;
    },
    /**
     * Settles one case that ran: `failure` is null when it passed, `"skipped"` when the client lacked what it needs, or
     * the failure. Answers `pass`, `skip`, `expected`, or `fail` with a `note` when the failure is listed as another.
     * A listed case that passed or was skipped adds a problem.
     */
    settle(name, failure) {
      const entry = entryOf.get(name);
      if (entry === undefined) return { verdict: failure === null ? "pass" : failure === "skipped" ? "skip" : "fail" };
      if (failure === null) {
        problems.push(`${name} is listed as failing but passed; remove ${label(entry)}`);
        return { verdict: "pass" };
      }
      if (failure === "skipped") {
        problems.push(`${name} is listed as failing but was skipped, which the allowlist of what a client lacks covers; remove ${label(entry)}`);
        return { verdict: "skip" };
      }
      const got = detailOf(failure);
      if (entry[detail] !== undefined && !fits(entry[detail], got))
        return { verdict: "fail", note: `listed as failing with ${detail} ${entry[detail]}, it fails with ${got ?? "none"}` };
      expected += 1;
      return { verdict: "expected", reason: entry.reason };
    },
  };
}

/** The summary's tail: how many failures the list excused. */
export const expectedCount = (n) => `${n} expected failure${n === 1 ? "" : "s"}`;
