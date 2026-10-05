// Records the Recovery sample's runs for traxsharp.net, which plays them back on its landing page. Start the
// sample's host as its README says (fresh data gives clean execution ids; the client is not needed), then run
//
//   node recovery.mjs [--copy-to <dir>]
//
// Each run is driven the way the sample's page drives it, over the same GraphQL operations, and everything the
// page would receive is written down with the time it arrived: the manifest's attempts, every junction event,
// each attempt's stored steps and its decision journal. One file per run lands in out/recovery/<key>.json. --copy-to also
// writes every run in out/recovery/ as one compact file, <dir>/recovery-recordings.json, with the two trains' source, which
// is how the website's copy is refreshed (--copy-to ../../../Trax.Website/src/data). --copy-only skips recording.
//
// node recovery.mjs <part-of-a-key> records only the runs whose key contains it.
//
// A run that forks (the user asks afresh or changes the data during the backoff, or asks afresh once the run is
// over) is recorded once per fork. Each recording marks where it forked, so the player can switch from one to
// another at that point.

import { createClient } from "graphql-ws";
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const HOST = process.env.RECOVERY_HOST ?? "localhost:5260";
const API_KEY = "recovery-operator-key-do-not-use-in-production";
const POLL_MS = 250;
const RANK = { IN_PROGRESS: 0, COMPLETED: 1, FAILED: 1, CANCELLED: 1 };
const ENDED = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

const STEP = `position kind name state startedAt endedAt durationMs failureClass failureException questionKey
  answer confidence replayed answerWithheld nameWithheld trackPosition attempt`;
const ROW = "id trainState startTime endTime failureJunction failureReason";

const ops = {
  startRun: `mutation ($input: StartRunInput!) { dispatch { startRun(input: $input) { output {
    runId manifestId manifestExternalId trainName armedCrash maxRetries } } } }`,
  changeCaseData: `mutation ($runId: String!) { dispatch { changeCaseData(input: { runId: $runId }) { output { change } } } }`,
  executions: `query ($manifestId: Long!) { operations { executions(manifestId: $manifestId, order: OLDEST, take: 20) {
    items { ${ROW} } } } }`,
  execution: `query ($id: Long!) { operations { execution(id: $id) { ${ROW} } } }`,
  junctionRuns: `query ($metadataId: Long!) { operations { junctionRuns(metadataId: $metadataId) { ${STEP} } } }`,
  journal: `query ($metadataId: Long!) { discover { decisionJournal(input: { metadataId: $metadataId }) {
    replayDecisionsOf replayAbandoned decisions { questionKey occurrence replayed replayRefused model stateHash } } } }`,
  triggerAskAfresh: `mutation ($externalId: String!) { operations { triggerManifest(externalId: $externalId, askAfresh: true) {
    success message } } }`,
  requeue: `mutation ($id: Long!) { operations { requeueExecution(id: $id, askAfresh: true) { success message id } } }`,
  workQueueEntry: `query ($id: Long!) { operations { workQueue { workQueue(id: $id) { id status metadataId } } } }`,
  onJunctionEvent: `subscription ($metadataId: Long!) { onJunctionEvent(metadataId: $metadataId) { junction { ${STEP} } } }`,
};

const TOPICS = { papers: "What the papers say about cold-weather battery wear", wiki: "History of the telegraph" };
const ORDERS = ["A-1001", "A-1002", "A-1003"];
const FORKS = ["none", "askAfresh", "changeData"];

/** Every run the player can show: each subject, clean or crashing, and each fork a crashing run can take. */
function plans() {
  const all = [];
  const subjects = [
    ...Object.entries(TOPICS).map(([key, topic]) => ({ key: `research-${key}`, input: { scenario: "RESEARCH", topic } })),
    ...ORDERS.map((orderId) => ({ key: `refund-${orderId}`, input: { scenario: "REFUND", orderId } })),
  ];
  for (const s of subjects) {
    all.push({ key: `${s.key}-clean`, input: { ...s.input, crashOnce: false }, fork: "none" });
    for (const fork of FORKS)
      all.push({ key: `${s.key}-crash-${fork}`, input: { ...s.input, crashOnce: true }, fork });
  }
  return all;
}

async function gql(query, variables) {
  const response = await fetch(`http://${HOST}/trax/graphql`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": API_KEY },
    body: JSON.stringify({ query, variables }),
  });
  const body = await response.json();
  if (body.errors?.length) throw new Error(`${body.errors[0].message} (${query.slice(0, 60)}...)`);
  return body.data;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function record(plan, ws) {
  const t0 = Date.now();
  const events = [];
  const log = (type, data) => events.push({ t: Date.now() - t0, type, ...data });

  const attempts = new Map(); // id -> { row, origin, steps: Map<position, step>, journal }
  const subscriptions = [];

  const mergeStep = (id, step) => {
    const attempt = attempts.get(id);
    const known = attempt.steps.get(step.position);
    if (known && RANK[known.state] >= RANK[step.state]) return;
    attempt.steps.set(step.position, step);
    log("step", { attemptId: id, step });
  };

  const follow = async (row, origin) => {
    if (attempts.has(row.id)) return;
    attempts.set(row.id, { row, origin, steps: new Map(), journal: null, journaled: false });
    log("attempt", { row, origin });
    // Subscribe first, then read what was stored before the subscription started, as the page does.
    subscriptions.push(
      ws.subscribe(
        { query: ops.onJunctionEvent, variables: { metadataId: row.id } },
        {
          next: ({ data }) => data?.onJunctionEvent && mergeStep(row.id, data.onJunctionEvent.junction),
          error: (e) => console.error(`  subscription ${row.id}:`, e),
          complete: () => {},
        },
      ),
    );
    await sleep(100);
    const stored = await gql(ops.junctionRuns, { metadataId: row.id });
    for (const step of stored.operations.junctionRuns) mergeStep(row.id, step);
  };

  const update = async (row) => {
    const attempt = attempts.get(row.id);
    if (!attempt) return;
    if (attempt.row.trainState !== row.trainState || attempt.row.endTime !== row.endTime) {
      attempt.row = row;
      log("row", { row });
    }
    if (ENDED.has(row.trainState) && !attempt.journaled) {
      attempt.journaled = true;
      const stored = await gql(ops.junctionRuns, { metadataId: row.id });
      for (const step of stored.operations.junctionRuns) mergeStep(row.id, step);
      const { discover } = await gql(ops.journal, { metadataId: row.id });
      attempt.journal = discover.decisionJournal;
      log("journal", { attemptId: row.id, journal: discover.decisionJournal });
    }
  };

  const { dispatch } = await gql(ops.startRun, { input: plan.input });
  const run = { ...dispatch.startRun.output, scenario: plan.input.scenario };
  log("started", { run });

  const manifestAttempts = () => [...attempts.values()].filter((a) => a.origin === "manifest");
  const settled = () => [...attempts.values()].every((a) => a.journal);
  let forked = false;
  let requeued = null;
  const deadline = t0 + 90_000;

  while (Date.now() < deadline) {
    const { operations } = await gql(ops.executions, { manifestId: run.manifestId });
    for (const row of operations.executions.items) {
      await follow(row, "manifest");
      await update(row);
    }
    if (requeued) {
      const one = await gql(ops.execution, { id: requeued });
      if (one.operations.execution) await update(one.operations.execution);
    }

    const manifest = manifestAttempts();
    const last = manifest[manifest.length - 1];

    // The backoff: the first attempt failed and its retry has not started. The page's buttons act here.
    if (!forked && manifest.length === 1 && last.journal && last.row.trainState === "FAILED") {
      forked = true;
      log("fork", { at: "backoff" });
      if (plan.fork === "none") continue;
      await sleep(700);
      if (plan.fork === "askAfresh") {
        const { operations: o } = await gql(ops.triggerAskAfresh, { externalId: run.manifestExternalId });
        log("action", { action: "askAfresh", via: "triggerManifest", message: o.triggerManifest.message });
      } else {
        const { dispatch: d } = await gql(ops.changeCaseData, { runId: run.runId });
        log("action", { action: "changeData", change: d.changeCaseData.output.change });
      }
    }

    // The run is over. Mark it, wait as a person would, and ask afresh once: a requeue of the last execution.
    if (last?.row.trainState === "COMPLETED" && settled() && !requeued) {
      log("fork", { at: "done" });
      await sleep(1200);
      const { operations: o } = await gql(ops.requeue, { id: last.row.id });
      const result = o.requeueExecution;
      log("action", { action: "requeue", sourceId: last.row.id, message: result.message });
      if (!result.success) throw new Error(`requeue refused: ${result.message}`);
      for (let i = 0; i < 120 && !requeued; i++) {
        const entry = await gql(ops.workQueueEntry, { id: result.id });
        const metadataId = entry.operations.workQueue.workQueue?.metadataId;
        if (metadataId) {
          requeued = metadataId;
          const one = await gql(ops.execution, { id: metadataId });
          await follow(one.operations.execution, "requeue");
        } else await sleep(POLL_MS);
      }
    }

    if (requeued && attempts.get(requeued)?.journal) break;
    await sleep(POLL_MS);
  }

  subscriptions.forEach((unsubscribe) => unsubscribe());
  if (!requeued || !attempts.get(requeued)?.journal) throw new Error(`${plan.key} did not finish in time`);
  log("end", {});
  return { key: plan.key, input: plan.input, fork: plan.fork, startedAt: new Date(t0).toISOString(), events };
}

const SOURCES = {
  RESEARCH: "Trains/Research/ResearchTopicTrain.cs",
  REFUND: "Trains/Refund/ApproveRefundTrain.cs",
};

/**
 * The website's copy of one run: times as milliseconds from the start, only the fields the player shows, and
 * execution ids numbered from 1041 in the order they appeared. The real ids differ from run to run (the
 * scheduler's own runs take ids too); numbering them the same way in every recording lets the player switch from
 * one fork to another without the ids on screen changing.
 */
function compact(recording) {
  const t0 = Date.parse(recording.startedAt);
  const ms = (iso) => (iso ? Date.parse(iso) - t0 : null);
  const ids = new Map();
  const id = (real) => {
    if (real == null) return null;
    if (!ids.has(real)) ids.set(real, 1041 + ids.size);
    return ids.get(real);
  };
  const prune = (o) => Object.fromEntries(Object.entries(o).filter(([, v]) => v != null && v !== false));
  const row = (r) =>
    prune({ id: id(r.id), trainState: r.trainState, start: ms(r.startTime), end: ms(r.endTime), failureJunction: r.failureJunction });
  const step = (s) =>
    prune({
      position: s.position,
      kind: s.kind,
      name: s.name,
      state: s.state,
      start: ms(s.startedAt),
      end: ms(s.endedAt),
      durationMs: s.durationMs == null ? null : Math.round(s.durationMs),
      failureException: s.failureException,
      questionKey: s.questionKey,
      answer: s.answer,
      confidence: s.confidence,
      replayed: s.replayed,
    });

  const events = recording.events.map(({ t, type, ...e }) => {
    switch (type) {
      case "started":
        return { t, type, run: { trainName: e.run.trainName, armedCrash: e.run.armedCrash, maxRetries: e.run.maxRetries } };
      case "attempt":
        return { t, type, row: row(e.row), origin: e.origin };
      case "row":
        return { t, type, row: row(e.row) };
      case "step":
        return { t, type, attemptId: id(e.attemptId), step: step(e.step) };
      case "journal":
        return {
          t,
          type,
          attemptId: id(e.attemptId),
          journal: prune({
            replayDecisionsOf: id(e.journal.replayDecisionsOf),
            replayAbandoned: e.journal.replayAbandoned,
            decisions: e.journal.decisions.map((d) =>
              prune({ questionKey: d.questionKey, replayed: d.replayed, replayRefused: d.replayRefused, model: d.model, stateHash: d.stateHash }),
            ),
          }),
        };
      case "action":
        return prune({ t, type, action: e.action, change: e.change, message: e.message, sourceId: id(e.sourceId) });
      default:
        return { t, type, ...e };
    }
  });
  const { scenario, topic, orderId, crashOnce } = recording.input;
  return prune({ key: recording.key, scenario, topic, orderId, crashOnce, fork: recording.fork, events });
}

function writeWebsiteCopy(dir) {
  const out = join(here, "out", "recovery");
  const recordings = readdirSync(out)
    .filter((f) => f.endsWith(".json"))
    .sort()
    .map((f) => compact(JSON.parse(readFileSync(join(out, f), "utf8"))));
  const project = resolve(here, "../../samples/Recovery/Trax.Samples.Recovery");
  const sources = Object.fromEntries(
    Object.entries(SOURCES).map(([scenario, file]) => [
      scenario,
      { file, code: readFileSync(join(project, file), "utf8").replace(/\r/g, "") },
    ]),
  );
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "recovery-recordings.json");
  writeFileSync(file, JSON.stringify({ sources, recordings }) + "\n");
  console.log(`wrote ${file}: ${recordings.length} recordings`);
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  const only = args.find((a, i) => !a.startsWith("--") && args[i - 1] !== "--copy-to");
  if (args.includes("--copy-only")) {
    if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
    return writeWebsiteCopy(copyTo);
  }

  const ws = createClient({ url: `ws://${HOST}/trax/graphql`, connectionParams: { apiKey: API_KEY } });
  const out = join(here, "out", "recovery");
  mkdirSync(out, { recursive: true });

  const todo = plans().filter((p) => !only || p.key.includes(only));
  // A few at a time: each run spends most of its time waiting on the model, a step or the backoff.
  for (let i = 0; i < todo.length; i += 4) {
    await Promise.all(
      todo.slice(i, i + 4).map(async (plan) => {
        const recording = await record(plan, ws);
        writeFileSync(join(out, `${plan.key}.json`), JSON.stringify(recording, null, 2));
        console.log(`recorded ${plan.key}: ${recording.events.length} events`);
      }),
    );
  }
  await ws.dispose();

  if (copyTo) writeWebsiteCopy(copyTo);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
