import { spawn } from "node:child_process";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { expectedCount, judge, readExpectedFailures } from "./expected-failures.mjs";
import { answers, canonical, kitCases } from "./kit.mjs";

// The expected failures of each testee, by the name it is run under (--name).
const expectedFailuresDir = join(fileURLToPath(new URL(".", import.meta.url)), "expected-failures");

const USAGE =
  "Usage: node conformance/run.mjs [--only <prefix>] [--name <testee>] (--testee <command> [args...] | --stdio)\n" +
  "See conformance/PROTOCOL.md.";

/**
 * Sends each case's request through `exchange` (request → Promise<answer>) and compares the answer, without its id,
 * with what the case expects (`answers` in kit.mjs). A machine whose load failed fails its remaining cases without sending them. Returns
 * the failures, each `{ name, request, expect, got }`; an empty list means the testee passed.
 */
export async function runCases(cases, exchange) {
  const failures = [];
  const unloaded = new Set();
  let id = 0;
  for (const c of cases) {
    if (c.request.machine !== undefined && unloaded.has(c.request.machine)) {
      failures.push({ ...c, got: { error: `machine ${c.request.machine} did not load` } });
      continue;
    }
    id += 1;
    const { id: answered, ...got } = await exchange({ id, ...c.request });
    if (answered !== id) throw new Error(`the testee answered request ${id} with id ${answered}`);
    if (!answers(got, c.expect)) {
      failures.push({ ...c, got });
      if (c.request.op === "load") unloaded.add(c.request.machine);
    }
  }
  return failures;
}

/**
 * The code a failed case's answer fails with, for an expected failure that names one: `unsupported`, the answer's
 * `reason` or `code`, `error` for an `error` answer (a machine that did not load among them), or null for an answer that
 * carries none (a wrong wire, a wrong plan).
 */
export function failureCode(got) {
  if (got?.unsupported === true) return "unsupported";
  for (const key of ["reason", "code"]) if (typeof got?.[key] === "string") return got[key];
  if (typeof got?.error === "string") return "error";
  return null;
}

/**
 * Judges the cases that ran against a testee's expected failures (expected-failures.mjs): `all` is every case of the
 * kit, `ran` the ones sent, `failures` what runCases answered. Returns the failures that stand, each with a `note` when
 * it is listed as failing another way, how many the list excused, and the problems with the list itself.
 */
export function verdict(entries, all, ran, failures) {
  const judged = judge(entries, all.map((c) => c.name), {
    detail: "code",
    detailOf: (f) => failureCode(f.got),
    fits: (listed, got) => listed === got,
  });
  const failed = new Map(failures.map((f) => [f.name, f]));
  const standing = [];
  for (const c of ran) {
    const failure = failed.get(c.name) ?? null;
    const { verdict: v, note } = judged.settle(c.name, failure);
    if (v === "fail") standing.push(note === undefined ? failure : { ...failure, note });
  }
  return { failures: standing, expected: judged.expected, problems: judged.problems };
}

/**
 * One line per failure, naming the case, then what it expected and what the testee answered; one per problem with the
 * expected failures; then the count. `expected` cases are left out of it, as they did not pass.
 */
export function report(failures, total, { expected = 0, problems = [] } = {}) {
  const lines = failures.map(
    (f) =>
      `FAIL ${f.name}\n  expected ${canonical(f.expect)}\n  got      ${canonical(f.got)}` +
      (f.note === undefined ? "" : `\n  ${f.note}`),
  );
  for (const p of problems) lines.push(`FAIL expected failures: ${p}`);
  const ran = total - expected;
  lines.push(`${ran - failures.length} of ${ran} cases passed, ${expectedCount(expected)}.`);
  return lines.join("\n");
}

/**
 * The lines of a stream, split on "\n" only. node's readline also splits on U+2028 and U+2029, which JSON.stringify
 * leaves literal inside a string, so a case holding one would arrive cut in two.
 */
export async function* readLines(stream) {
  stream.setEncoding("utf8");
  let buffer = "";
  for await (const chunk of stream) {
    buffer += chunk;
    let end;
    while ((end = buffer.indexOf("\n")) >= 0) {
      yield buffer.slice(0, end).replace(/\r$/, "");
      buffer = buffer.slice(end + 1);
    }
  }
  if (buffer !== "") yield buffer;
}

// Requests go out as lines on `output`; answers come back as lines on `input`, one per request, in order.
function lineExchange(input, output) {
  const lines = readLines(input);
  return async (request) => {
    output.write(`${JSON.stringify(request)}\n`);
    const { value, done } = await lines.next();
    if (done) throw new Error(`the testee closed before answering request ${request.id} (${request.op})`);
    return JSON.parse(value);
  };
}

async function main(argv) {
  let only = "";
  let name;
  let mode;
  let command = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--only") only = argv[++i] ?? "";
    else if (argv[i] === "--name") name = argv[++i] ?? "";
    else if (argv[i] === "--stdio") mode = "stdio";
    else if (argv[i] === "--testee") {
      mode = "spawn";
      command = argv.slice(i + 1);
      break;
    } else throw new Error(`${USAGE}\nUnknown argument ${argv[i]}.`);
  }
  if (!mode || (mode === "spawn" && command.length === 0)) throw new Error(USAGE);

  const { entries } = readExpectedFailures(expectedFailuresDir, name, "code");
  const all = kitCases();
  const cases = all.filter((c) => c.name.startsWith(only) || c.request.op === "load");
  // A selection of nothing passes nothing: say so rather than report every case passed.
  if (!cases.some((c) => c.name.startsWith(only)))
    throw new Error(`No case of the kit is named with the prefix "${only}".`);
  let exchange;
  let child;
  if (mode === "spawn") {
    child = spawn(command[0], command.slice(1), { stdio: ["pipe", "pipe", "inherit"] });
    exchange = lineExchange(child.stdout, child.stdin);
  } else exchange = lineExchange(process.stdin, process.stdout);

  const judged = verdict(entries, all, cases, await runCases(cases, exchange));
  await exchange({ id: cases.length + 1, op: "end" });
  if (child) child.stdin.end();
  console.error(report(judged.failures, cases.length, judged));
  return judged.failures.length === 0 && judged.problems.length === 0 ? 0 : 1;
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (err) => {
      console.error(err.message);
      process.exit(2);
    },
  );
