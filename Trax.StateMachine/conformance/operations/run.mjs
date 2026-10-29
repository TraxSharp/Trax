import { createHash } from "node:crypto";
import { spawn } from "node:child_process";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { canonical } from "../kit.mjs";
import { startHost } from "../client/host.mjs";
import { lineAsker, testeeFailure, withTimeout } from "../client/run.mjs";

const USAGE =
  "Usage: node conformance/operations/run.mjs [--only <prefix>] (--testee <command> [args...] | --stdio)\n" +
  "See conformance/operations/PROTOCOL.md.";

const kitDir = fileURLToPath(new URL(".", import.meta.url));
const fixtureDir = join(kitDir, "fixture");
const transcriptsDir = join(kitDir, "transcripts");

// How long a step waits for the client before the transcript fails. Every timeout a transcript configures is far
// shorter, so only a client that never does what the step waits for reaches it.
const STEP_TIMEOUT = 3000;

const readJson = (path) => JSON.parse(readFileSync(path, "utf8"));
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const sha256 = (text) => createHash("sha256").update(text, "utf8").digest("hex");

/**
 * The id a host registers a console document under, from its name and text: `trax_console_<name>_<hash><check>`, the
 * hash the text's SHA-256 and the check the first 16 hex digits of the SHA-256 of the hash, a newline and the text.
 */
export const consoleIdOf = (name, text) => {
  const hash = sha256(text);
  return `trax_console_${name.replaceAll("_", "-")}_${hash}${sha256(`${hash}\n${text}`).slice(0, 16)}`;
};

/**
 * The documents the fake host serves: the fixture's `console-documents.json`, the file a host registers the console's
 * documents from, each with the id the host computes for it. `operation` is the document's name, which the host reports
 * each request under.
 */
export function kitDocuments() {
  return readJson(join(fixtureDir, "console-documents.json")).documents.map((d) => ({
    id: consoleIdOf(d.name, d.text),
    operation: d.name,
  }));
}

/** Every transcript, by name, in name order. */
export function transcripts() {
  return readdirSync(transcriptsDir)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((f) => ({ name: f.slice(0, -".json".length), ...readJson(join(transcriptsDir, f)) }));
}

// Whether every key `expected` names holds the same value in `got`, canonically.
const matches = (expected, got) =>
  got !== null &&
  typeof got === "object" &&
  Object.entries(expected).every(([k, v]) => canonical(v) === canonical(got[k] ?? null));

/**
 * Runs one transcript against the testee through `askTestee` (request → Promise<answer>). Throws an Error naming the
 * step that failed; returns when every step passed and nothing arrived that no step took.
 */
export async function runTranscript(transcript, host, askTestee) {
  const ask = (request) =>
    withTimeout(askTestee(request), `the testee's answer to ${request.op}`).then((answer) => {
      const failure = testeeFailure(answer);
      if (failure !== null) throw new Error(`the testee's answer to ${request.op}: ${failure}`);
      return answer;
    });
  const documents = kitDocuments();
  const idOf = new Map(documents.map((d) => [d.operation, d.id]));
  host.reset({ documents, refuse: transcript.refuseConnections ?? 0 });

  const opened = await ask({ op: "open", endpoint: host.endpoint, socket: host.socket, ...(transcript.open ?? {}) });
  if (transcript.opened !== undefined) {
    if (!matches(transcript.opened, opened))
      throw new Error(`open: expected ${canonical(transcript.opened)}, got ${canonical(opened)}`);
    if (opened.opened !== true) {
      await delay(30);
      const extra = host.leftovers();
      if (extra.length > 0) throw new Error(`a client that refused to open sent ${canonical(extra)}`);
      return;
    }
  } else if (opened.opened !== true) throw new Error(`open: ${canonical(opened)}`);

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
      if (request.operation !== step.request) fail(`expected a ${step.request} request, got ${request.operation}`);
      // A persisted document goes by the id the host registered, never with its text.
      if (request.body.id !== idOf.get(step.request)) fail(`expected the id ${idOf.get(step.request)}, got ${request.body.id}`);
      if (request.body.query !== undefined) fail(`the request sent a document's text beside its id: ${request.body.query}`);
      if (step.variables !== undefined && canonical(request.body.variables ?? {}) !== canonical(step.variables))
        fail(`expected variables ${canonical(step.variables)}, got ${canonical(request.body.variables ?? {})}`);
      if (step.hang) request.respond({ hang: true });
      else if (step.redirect !== undefined)
        request.respond({ status: step.redirect === true ? 307 : step.redirect, headers: { location: host.decoy } });
      else if (step.raw !== undefined) request.respond({ status: step.status ?? 200, raw: step.raw });
      else if (step.status !== undefined) request.respond({ status: step.status, json: step.body });
      else
        request.respond({
          json: {
            ...(step.data !== undefined || step.errors === undefined ? { data: step.data ?? null } : {}),
            ...(step.errors !== undefined ? { errors: step.errors } : {}),
          },
          pad: step.pad ?? 0,
        });
    } else if (step.subscribe !== undefined) {
      subscription = await host.subscriptions.take(STEP_TIMEOUT, "subscription").catch((e) => fail(e.message));
      if (subscription.operation !== step.subscribe)
        fail(`expected a ${step.subscribe} subscription, got ${subscription.payload.id}`);
      if (subscription.payload.query) fail(`the subscription sent a document's text beside its id: ${subscription.payload.query}`);
      if (step.variables !== undefined && canonical(subscription.payload.variables ?? {}) !== canonical(step.variables))
        fail(`expected variables ${canonical(step.variables)}, got ${canonical(subscription.payload.variables ?? {})}`);
    } else if (step.push !== undefined) {
      if (!subscription?.open) fail("no subscription is open to push on");
      subscription.next({ data: step.push }, step.pad ?? 0);
    } else if (step.pushErrors !== undefined) {
      if (!subscription?.open) fail("no subscription is open to push on");
      subscription.next({ data: null, errors: step.pushErrors });
    } else if (step.streamError !== undefined) {
      if (!subscription?.open) fail("no subscription is open to end");
      subscription.error(step.streamError);
    } else if (step.complete !== undefined) {
      if (!subscription?.open) fail("no subscription is open to complete");
      subscription.complete();
    } else if (step.dropSocket !== undefined) {
      if (!subscription) fail("no socket to drop");
      subscription.drop(step.dropSocket === true ? undefined : step.dropSocket);
    } else if (step.events !== undefined) {
      const deadline = Date.now() + STEP_TIMEOUT;
      let got;
      for (;;) {
        got = (await ask({ op: "events" })).events;
        if (canonical(got) === canonical(step.events)) break;
        if (Date.now() > deadline) fail(`expected the events ${canonical(step.events)}, got ${canonical(got)}`);
        await delay(5);
      }
    } else if (step.decoy !== undefined) {
      if (host.decoyHits() !== step.decoy) fail(`expected ${step.decoy} requests at the redirect's target, got ${host.decoyHits()}`);
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

/** Runs every transcript through `ask`; returns the failures, each `{ name, error }`. `onResult` hears each one. */
export async function runTranscripts(list, host, ask, onResult = () => {}) {
  const failures = [];
  for (const t of list) {
    try {
      await runTranscript(t, host, ask);
      onResult({ name: t.name });
    } catch (err) {
      failures.push({ name: t.name, error: err.message });
      onResult(failures[failures.length - 1]);
      await withTimeout(ask({ op: "dispose" }), "the testee's answer to dispose").catch(() => {});
      await delay(50);
    }
  }
  return failures;
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
  if (list.length === 0) throw new Error(`No transcript's name starts with "${only}".`);

  let ask;
  let child;
  if (mode === "spawn") {
    child = spawn(command[0], command.slice(1), { stdio: ["pipe", "pipe", "inherit"] });
    ask = lineAsker(child.stdout, child.stdin);
  } else ask = lineAsker(process.stdin, process.stdout);

  const host = await startHost();
  const failures = await runTranscripts(list, host, ask, (r) =>
    console.error(r.error !== undefined ? `FAIL ${r.name}\n  ${r.error}` : `ok   ${r.name}`),
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
