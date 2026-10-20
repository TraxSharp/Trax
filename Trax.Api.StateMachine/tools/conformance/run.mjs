import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";
import { canonical, kitCases } from "./kit.mjs";

const USAGE =
  "Usage: node tools/conformance/run.mjs [--only <prefix>] (--testee <command> [args...] | --stdio)\n" +
  "See conformance/PROTOCOL.md.";

/**
 * Sends each case's request through `exchange` (request → Promise<answer>) and compares the answer, without its id,
 * with what the case expects. A machine whose load failed fails its remaining cases without sending them. Returns
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
    if (canonical(got) !== canonical(c.expect)) {
      failures.push({ ...c, got });
      if (c.request.op === "load") unloaded.add(c.request.machine);
    }
  }
  return failures;
}

/** One line per failure, naming the case, then what it expected and what the testee answered. */
export function report(failures, total) {
  const lines = failures.map(
    (f) => `FAIL ${f.name}\n  expected ${canonical(f.expect)}\n  got      ${canonical(f.got)}`,
  );
  lines.push(`${total - failures.length} of ${total} cases passed.`);
  return lines.join("\n");
}

// Requests go out as lines on `output`; answers come back as lines on `input`, one per request, in order.
function lineExchange(input, output) {
  const lines = createInterface({ input })[Symbol.asyncIterator]();
  return async (request) => {
    output.write(`${JSON.stringify(request)}\n`);
    const { value, done } = await lines.next();
    if (done) throw new Error(`the testee closed before answering request ${request.id} (${request.op})`);
    return JSON.parse(value);
  };
}

async function main(argv) {
  let only = "";
  let mode;
  let command = [];
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--only") only = argv[++i] ?? "";
    else if (argv[i] === "--stdio") mode = "stdio";
    else if (argv[i] === "--testee") {
      mode = "spawn";
      command = argv.slice(i + 1);
      break;
    } else throw new Error(`${USAGE}\nUnknown argument ${argv[i]}.`);
  }
  if (!mode || (mode === "spawn" && command.length === 0)) throw new Error(USAGE);

  const cases = kitCases().filter((c) => c.name.startsWith(only) || c.request.op === "load");
  let exchange;
  let child;
  if (mode === "spawn") {
    child = spawn(command[0], command.slice(1), { stdio: ["pipe", "pipe", "inherit"] });
    exchange = lineExchange(child.stdout, child.stdin);
  } else exchange = lineExchange(process.stdin, process.stdout);

  const failures = await runCases(cases, exchange);
  await exchange({ id: cases.length + 1, op: "end" });
  if (child) child.stdin.end();
  console.error(report(failures, cases.length));
  return failures.length === 0 ? 0 : 1;
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (err) => {
      console.error(err.message);
      process.exit(2);
    },
  );
