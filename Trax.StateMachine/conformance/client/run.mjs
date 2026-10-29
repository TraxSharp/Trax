import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { canonical } from "../kit.mjs";
import { startHost } from "./host.mjs";

const USAGE =
  "Usage: node conformance/client/run.mjs [--only <prefix>] (--testee <command> [args...] | --stdio)\n" +
  "See conformance/client/PROTOCOL.md.";

const clientDir = fileURLToPath(new URL(".", import.meta.url));
const machinesDir = join(clientDir, "..", "machines");
// Machines only the client kit drives, beside the engine kit's: one IR file each, as the C# machine would export it.
const clientMachinesDir = join(clientDir, "machines");
const transcriptsDir = join(clientDir, "transcripts");

// How long a step waits for the client before the transcript fails. Every delay a transcript configures is far
// shorter, so only a client that never does what the step waits for reaches it.
const STEP_TIMEOUT = 3000;

const readJson = (path) => JSON.parse(readFileSync(path, "utf8"));

/** The machine's field under `machines`: its kebab-case id in camelCase (`topic-map` is `topicMap`). */
export const machineField = (id) =>
  id
    .split("-")
    .filter((p) => p.length > 0)
    .map((p, i) => (i === 0 ? p : p[0].toUpperCase() + p.slice(1)))
    .join("");

/**
 * A machine of the kit as a client gets it: its IR as committed, which is the text the C# machine exports, and that
 * text's SHA-256, the hash a host stamps its documents with and a generated twin carries. The client kit's own machines
 * come first, then the engine kit's.
 */
export function machineOf(id) {
  const own = join(clientMachinesDir, `${id}.ir.json`);
  const path = existsSync(own) ? own : join(machinesDir, id, `${id}.ir.json`);
  const text = readFileSync(path, "utf8").replace(/\n+$/, "");
  const irHash = createHash("sha256").update(text, "utf8").digest("hex");
  return { ir: JSON.parse(text), irHash };
}

/**
 * The persisted documents a host would export for the machine: one per operation the transcript lists, each declaring
 * the variables the transcript gives it and selecting the whole answer.
 */
export function documentsOf(machine, irHash, operations) {
  const field = machineField(machine);
  const answer = "id revision state context dueAt dueAtKnown problem { code message }";
  return Object.entries(operations).map(([operation, variables]) => {
    const args = [...variables.matchAll(/\$(\w+)\s*:/g)].map((m) => `${m[1]}: $${m[1]}`).join(", ");
    const call = args === "" ? operation : `${operation}(${args})`;
    const declared = variables === "" ? "" : `(${variables})`;
    const document =
      operation === "changed"
        ? `subscription Changed${declared} { ${field}Changed${args === "" ? "" : `(${args})`} { rowId revision state context dueAt dueAtKnown gone reload problem { code message } } }`
        : operation === "get"
          ? `query Get${declared} { machines { ${field} { ${call} { ${answer} } } } }`
          : operation === "discard"
            ? `mutation Discard${declared} { machines { ${field} { ${call} { id discarded problem { code message } } } } }`
            : `mutation Op${declared} { machines { ${field} { ${call} { ${answer} } } } }`;
    return { id: `trax_machine_${machine}_${operation}_${irHash}`, machine, operation, irHash, document };
  });
}

/** Every transcript, by name, in name order. */
export function transcripts() {
  return readdirSync(transcriptsDir)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((f) => ({ name: f.slice(0, -".json".length), ...readJson(join(transcriptsDir, f)) }));
}

// What a real host sends beside an answer's own fields, so a transcript writes only what matters to it.
const ANSWER_DEFAULTS = { dueAt: null, dueAtKnown: true, problem: null };
const PROBLEM_DEFAULTS = { id: null, revision: null, state: null, context: null, dueAt: null, dueAtKnown: true };
const CHANGE_DEFAULTS = { rowId: null, dueAt: null, dueAtKnown: true, gone: false, reload: false, problem: null };

function answerOf(value) {
  if (value === null) return null;
  return value.problem ? { ...PROBLEM_DEFAULTS, ...value } : { ...ANSWER_DEFAULTS, ...value };
}

// Whether every key `expected` names holds the same value in `got`. A due time is compared as the instant it names: a
// client may hold it in another offset than the host wrote, or with more digits.
const matches = (expected, got) =>
  got !== null &&
  typeof got === "object" &&
  Object.entries(expected).every(([k, v]) =>
    k === "dueAt" ? sameInstant(v, got[k] ?? null) : canonical(v) === canonical(got[k] ?? null),
  );

function sameInstant(expected, got) {
  if (typeof expected !== "string" || typeof got !== "string") return canonical(expected) === canonical(got);
  const [a, b] = [Date.parse(expected), Date.parse(got)];
  return !Number.isNaN(a) && a === b;
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Runs one transcript against the testee through `ask` (request → Promise<answer>). Throws an Error naming the step
 * that failed; returns when every step passed and nothing arrived that no step took.
 */
export async function runTranscript(transcript, host, askTestee) {
  // Every answer comes within the step timeout, or the transcript fails naming the request it waited on, and no answer
  // may be an `error` or `unsupported`, the ones no step compares included.
  const ask = (request) => withTimeout(askTestee(request), `the testee's answer to ${request.op}`).then(checked(request));
  const { ir, irHash } = machineOf(transcript.machine);
  const documents = documentsOf(transcript.machine, irHash, transcript.documents);
  const field = machineField(transcript.machine);
  host.reset({ documents, refuse: transcript.refuseConnections ?? 0, refuseUpgrades: transcript.refuseUpgrades ?? [] });

  const opened = await ask({
    op: "open",
    machine: transcript.machine,
    ir,
    irHash,
    documents,
    endpoint: host.endpoint,
    socket: host.socket,
    instance: transcript.instance ?? null,
    startInput: transcript.startInput ?? null,
    effectEvent: transcript.effectEvent ?? null,
    retryDelays: transcript.retryDelays ?? [10, 10],
    resubscribeDelays: transcript.resubscribeDelays ?? [10],
    ...(transcript.open ?? {}),
  });
  // A transcript that expects the client to refuse to open compares that answer and has no steps to run.
  if (transcript.opened !== undefined) {
    if (!matches(transcript.opened, opened)) throw new Error(`open: expected ${canonical(transcript.opened)}, got ${canonical(opened)}`);
    if (opened.opened !== true) return;
  } else if (!opened.opened) throw new Error(`open: ${opened.error ?? JSON.stringify(opened)}`);

  // Answers a step names to await later, and answers no step looks at, which must still come.
  const pending = new Map();
  const background = [];
  let subscription = null;

  for (const [index, step] of transcript.steps.entries()) {
    const where = `step ${index + 1} (${JSON.stringify(step).slice(0, 80)})`;
    const fail = (message) => {
      throw new Error(`${where}: ${message}`);
    };

    if (step.client !== undefined) {
      const { client: op, as, expect, ...args } = step;
      const answer = ask({ op, ...args });
      // Held until awaited: the timeout's rejection is not unhandled while the steps between run.
      answer.catch(() => {});
      if (as !== undefined) pending.set(as, answer);
      else if (expect === undefined) background.push(answer);
      else {
        const got = await answer;
        if (!matches(expect, got)) fail(`expected ${canonical(expect)}, got ${canonical(got)}`);
      }
    } else if (step.await !== undefined) {
      const answer = pending.get(step.await) ?? fail(`nothing named ${step.await} was sent`);
      pending.delete(step.await);
      const got = await answer;
      if (!matches(step.expect ?? {}, got)) fail(`expected ${canonical(step.expect)}, got ${canonical(got)}`);
    } else if (step.request !== undefined) {
      const request = await host.requests.take(STEP_TIMEOUT, `${step.request} request`).catch((e) => fail(e.message));
      if (request.operation !== step.request)
        fail(`expected a ${step.request} request, got ${request.operation} ${canonical(request.body.variables ?? {})}`);
      if (step.variables !== undefined && canonical(request.body.variables ?? {}) !== canonical(step.variables))
        fail(`expected variables ${canonical(step.variables)}, got ${canonical(request.body.variables ?? {})}`);
      if (step.drop) request.respond({ close: true });
      else if (step.hang) request.respond({ hang: true });
      else if (step.redirect !== undefined)
        request.respond({ status: step.redirect === true ? 307 : step.redirect, headers: { location: host.decoy } });
      else if (step.raw !== undefined) request.respond({ raw: step.raw });
      else if (step.status !== undefined) request.respond({ status: step.status, json: step.body });
      else if (step.errors !== undefined) request.respond({ json: { data: null, errors: step.errors } });
      else
        request.respond({
          json: { data: { machines: { [field]: { [step.request]: answerOf(step.answer ?? null) } } } },
          pad: step.pad ?? 0,
        });
    } else if (step.subscribe !== undefined) {
      subscription = await host.subscriptions.take(STEP_TIMEOUT, "subscription").catch((e) => fail(e.message));
      if (subscription.operation !== "changed") fail(`expected the changed document, got ${subscription.payload.id}`);
      if (subscription.payload.query) fail(`the subscription sent a document beside its id: ${subscription.payload.query}`);
      if (canonical(subscription.payload.variables ?? {}) !== canonical(step.subscribe))
        fail(`expected variables ${canonical(step.subscribe)}, got ${canonical(subscription.payload.variables ?? {})}`);
    } else if (step.push !== undefined) {
      if (!subscription?.open) fail("no subscription is open to push on");
      subscription.next({ data: { [`${field}Changed`]: { ...CHANGE_DEFAULTS, ...step.push } } }, step.pad ?? 0);
    } else if (step.pushErrors !== undefined) {
      if (!subscription?.open) fail("no subscription is open to push on");
      subscription.next({ data: null, errors: step.pushErrors });
    } else if (step.complete !== undefined) {
      if (!subscription?.open) fail("no subscription is open to complete");
      subscription.complete();
    } else if (step.dropSocket !== undefined) {
      if (!subscription) fail("no socket to drop");
      subscription.drop(step.dropSocket === true ? undefined : step.dropSocket);
    } else if (step.ping !== undefined) {
      if (!subscription) fail("no socket to ping");
      const before = subscription.pongs;
      subscription.ping();
      const deadline = Date.now() + STEP_TIMEOUT;
      while (subscription.pongs === before) {
        if (Date.now() > deadline) fail("the client did not answer the host's ping with a pong");
        await delay(5);
      }
    } else if (step.decoy !== undefined) {
      if (host.decoyHits() !== step.decoy) fail(`expected ${step.decoy} requests at the redirect's target, got ${host.decoyHits()}`);
    } else if (step.view !== undefined) {
      const deadline = Date.now() + STEP_TIMEOUT;
      let got;
      for (;;) {
        got = await ask({ op: "view" });
        if (matches(step.view, got)) break;
        if (Date.now() > deadline) fail(`expected the view to hold ${canonical(step.view)}, it holds ${canonical(got)}`);
        await delay(5);
      }
    } else if (step.wait !== undefined) {
      await delay(step.wait);
    } else if (step.quiet !== undefined) {
      await delay(step.quiet);
      const extra = host.leftovers();
      if (extra.length > 0) fail(`expected nothing to arrive, got ${canonical(extra)}`);
    } else fail("unknown step");
  }

  if (pending.size > 0) throw new Error(`no step awaited ${[...pending.keys()].join(", ")}`);
  await Promise.all(background);
  await delay(30);
  const extra = host.leftovers();
  const disposed = await ask({ op: "dispose" });
  if (disposed.disposed !== true) throw new Error(`dispose: expected { "disposed": true }, got ${canonical(disposed)}`);
  if (extra.length > 0) throw new Error(`after the last step: nothing should have arrived, got ${canonical(extra)}`);
}

/**
 * Fails an answer that says the testee went wrong rather than its client: `unsupported`, or an `error` text. A view's
 * `error` is the code that stopped the client, beside its `status`, and is not one.
 */
export function testeeFailure(answer) {
  if (answer === null || typeof answer !== "object") return "not an object";
  if (answer.unsupported === true) return "unsupported";
  if (typeof answer.error === "string" && answer.status === undefined) return `error: ${answer.error}`;
  return null;
}

const checked = (request) => (answer) => {
  const failure = testeeFailure(answer);
  if (failure !== null) throw new Error(`the testee's answer to ${request.op}: ${failure}`);
  return answer;
};

function withTimeout(promise, what) {
  let timer;
  return Promise.race([
    promise,
    new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error(`${what} did not come within ${STEP_TIMEOUT} ms`)), STEP_TIMEOUT);
    }),
  ]).finally(() => clearTimeout(timer));
}

/**
 * Runs every transcript through `exchange` (a sender of request lines and a reader of answer lines, matched by id).
 * Returns the failures, each `{ name, error }`.
 */
export async function runTranscripts(list, host, ask, onResult = () => {}) {
  const failures = [];
  for (const t of list) {
    try {
      await runTranscript(t, host, ask);
      onResult({ name: t.name });
    } catch (err) {
      failures.push({ name: t.name, error: err.message });
      onResult(failures[failures.length - 1]);
      // A transcript that failed may have left its controller mid-flight: drop it before the next one opens.
      await withTimeout(ask({ op: "dispose" }), "the testee's answer to dispose").catch(() => {});
    }
  }
  return failures;
}

// Requests go out as lines on `output`; answers come back as lines on `input`, matched to their request by id,
// since an answer to `send` comes only once the host answers it, after later requests.
// TRAX_CLIENT_KIT_TRACE=1 writes every request and answer on stderr.
const trace = process.env.TRAX_CLIENT_KIT_TRACE ? (line) => console.error(line.slice(0, 300)) : () => {};

/**
 * Calls `onLine` with every line of `input`, split on "\n" only: readline would also split on U+2028 and U+2029, which
 * JSON writes raw inside a string. Calls `onClose` once the input ends.
 */
export function readLines(input, onLine, onClose) {
  let rest = "";
  input.setEncoding("utf8");
  input.on("data", (chunk) => {
    const lines = (rest + chunk).split("\n");
    rest = lines.pop();
    for (const line of lines) if (line.trim() !== "") onLine(line);
  });
  let ended = false;
  const end = () => {
    if (ended) return;
    ended = true;
    if (rest.trim() !== "") onLine(rest);
    rest = "";
    onClose();
  };
  input.on("end", end);
  input.on("close", end);
}

function lineAsker(input, output) {
  const waiting = new Map();
  let next = 0;
  let closed = false;
  readLines(
    input,
    (line) => {
      trace(`<- ${line}`);
      const answer = JSON.parse(line);
      const resolve = waiting.get(answer.id);
      waiting.delete(answer.id);
      resolve?.(answer);
    },
    () => {
      closed = true;
      for (const resolve of waiting.values()) resolve({ error: "the testee closed" });
      waiting.clear();
    },
  );
  return (request) => {
    if (closed) return Promise.resolve({ error: "the testee closed" });
    next += 1;
    const id = next;
    return new Promise((resolve) => {
      waiting.set(id, (answer) => {
        const { id: _, ...rest } = answer;
        resolve(rest);
      });
      const line = JSON.stringify({ id, ...request });
      trace(`-> ${line}`);
      output.write(`${line}\n`);
    });
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
  const list = transcripts().filter((t) => t.name.startsWith(only));
  // A selection that runs nothing passes nothing: it is a mistake, not a pass.
  if (list.length === 0) throw new Error(`No transcript's name starts with "${only}".`);

  let ask;
  let child;
  if (mode === "spawn") {
    child = spawn(command[0], command.slice(1), { stdio: ["pipe", "pipe", "inherit"] });
    ask = lineAsker(child.stdout, child.stdin);
  } else ask = lineAsker(process.stdin, process.stdout);

  const host = await startHost();
  // Each transcript is reported as it ends, so a testee that hangs still shows how far it got.
  const failures = await runTranscripts(list, host, ask, (r) =>
    console.error(r.error === undefined ? `ok   ${r.name}` : `FAIL ${r.name}\n  ${r.error}`),
  );
  const ended = await withTimeout(ask({ op: "end" }), "the testee's answer to end").catch((e) => ({ error: e.message }));
  if (child) child.stdin.end();
  await host.close();

  const endFailure = testeeFailure(ended);
  if (endFailure !== null) console.error(`FAIL end\n  the testee's answer to end: ${endFailure}`);
  console.error(`${list.length - failures.length} of ${list.length} transcripts passed.`);
  return failures.length === 0 && endFailure === null ? 0 : 1;
}

if (process.argv[1] === fileURLToPath(import.meta.url))
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (err) => {
      console.error(err.message);
      process.exit(2);
    },
  );
