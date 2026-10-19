// Records the operations dashboard for traxsharp.net, which serves it as a demo that answers from these recordings.
// Unlike the other scripts, this one starts the hosts itself, because it needs three of them on databases of their
// own: build nothing first, have Docker's Postgres up (the compose file's `database`), then run
//
//   node dashboard.mjs [--copy-to <dir>] [--dashboard <dir>]
//
// It reads every query, mutation and subscription document from the dashboard's source (--dashboard, default
// ../../../Trax.Api/src/Trax.Api.Dashboard/ClientApp), so what it records is exactly what the dashboard sends.
//
// Three samples feed it, each on a private port and a fresh database it creates in the Postgres container
// (TRAX_PG_CONTAINER, default trax_database, published on TRAX_PG_PORT, default 5432). Beside the database in
// one pod or network, TRAX_PSQL=psql creates them with a psql of its own instead, on TRAX_PG_HOST:TRAX_PG_PORT.
//
//   Scheduling           interval, cron, one-off, dependent and dormant manifests, a group per manifest and the
//                        dependency graph between them, a supplier outage long enough for three dead letters (one
//                        requeued, one acknowledged, one left awaiting), the work queue, trains' logs, the
//                        scheduler settings, runtime log levels and effects. It is the host the dashboard is
//                        connected to.
//   Recovery             runs that ask a model, crash and retry: junction steps, run graphs, recorded decisions
//                        (one asked afresh after its data changed, one whose state holds a sensitive value), and a
//                        requeue that replays a run's decisions. And its three state machines: the six
//                        system-owned index partitions discovery starts (one held for review), three food orders
//                        (delivered, cancelled by a closed restaurant, left in its cart with its timer due) and two
//                        topic map drafts (built, and one left choosing its years), each with its history.
//   PersistedOperations  the store the dashboard manages: uploads, a hot-fix with history, a refused shape change,
//                        a deactivated operation and a tenant's own copy.
//
// The run has four phases. Drive: the hosts run and are driven for about three minutes while every
// onTrainStateChanged, onDataChanged and onJunctionEvent frame is written down with the time it arrived. Freeze:
// both schedulers' manifest manager and dispatcher are switched off (the dashboard's own updateScheduler), so the
// data stops moving, two entries are queued that will now wait, and two bulk operations disable and enable the
// refresh train's manifests. Snapshot, of the next whole minute: every list the dashboard can show is walked to its
// end, once for each filter, sort and scope a control offers, and every detail, stats, timeline, run graph, series
// and schedule is read for every row, as is every state machine's instance list under each filter, and every
// instance's page and history. Mutations: every write the dashboard offers is sent to a real host, so the demo can say
// what Trax says, refusals included.
//
// The demo shows one Trax cluster, so the hosts are combined as if they shared a database: what a database holds
// (runs, manifests, groups, the work queue, dead letters, logs, decisions, junction steps, hosts, metrics, the
// persisted-operations store) is the union of what each host returned, and what a process holds (its trains,
// effects, settings, log levels, environment and version) comes from Scheduling, with Recovery's trains and effects
// added. Ids are renumbered in one sequence per kind, interleaved by time where the row has one, so a list mixes
// both hosts newest first and every reference (a run's manifest, a dead letter's retry, a replay's source) points
// at the renumbered row. A list both hosts answer is the union of their answers for the same filter, cut into the
// dashboard's page size the way the API cuts it (keyset, the last id as the cursor); a list scoped to one host's
// row (a manifest's runs, a run's log) is that host's answer.
//
// The raw recording lands in out/dashboard/recording.json. --copy-to writes the website's copy,
// <dir>/dashboard-recordings.json (--copy-to ../../../Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/demo/data), and --copy-only
// rewrites it from out/ without starting anything. The copy differs from what the hosts sent in four ways: ids are
// renumbered as above; the machine's name, process ids and file paths are replaced (hosts are named after the
// sample they ran, and stack traces start at the repository); the version the footer shows is the Trax.Api version
// this repository last released, where a local build reports 1.99.99; and rows that repeat across pages are stored once.

import { createClient } from "graphql-ws";
import { execFileSync, spawn } from "node:child_process";
import { createServer } from "node:net";
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, writeFileSync } from "node:fs";
import { homedir, hostname, userInfo } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");
const out = join(here, "out", "dashboard");
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

const args = process.argv.slice(2);
const argValue = (flag) => {
  const at = args.indexOf(flag);
  return at >= 0 ? resolve(process.cwd(), args[at + 1]) : null;
};
const dashboardDir = argValue("--dashboard") ?? resolve(repo, "../Trax.Api/src/Trax.Api.Dashboard/ClientApp");
const copyTo = argValue("--copy-to");

const PG_CONTAINER = process.env.TRAX_PG_CONTAINER ?? "trax_database";
const PG_HOST = process.env.TRAX_PG_HOST || "localhost";
const PG_PORT = process.env.TRAX_PG_PORT || "5432";
// How to reach psql: through the container by default, or a psql of its own (TRAX_PSQL=psql, which then connects
// to TRAX_PG_HOST:TRAX_PG_PORT), as when the recorder runs beside the database in one pod.
const PSQL = process.env.TRAX_PSQL
  ? [...process.env.TRAX_PSQL.split(" "), "-h", PG_HOST, "-p", PG_PORT]
  : ["docker", "exec", PG_CONTAINER, "psql"];

// The order is the order ids are interleaved in when two rows have no time to compare.
const HOSTS = {
  scheduling: {
    alias: "scheduling",
    project: "samples/Scheduling/Trax.Samples.Scheduling.Host",
    port: 5391,
    database: "trax_dashboard_recording_scheduling",
    key: "operator-key-do-not-use-in-production",
    // Nine failing calls: three rounds of a run and its two retries, so three dead letters.
    env: { SupplierFeed__OutageCalls: "9" },
  },
  recovery: {
    alias: "recovery",
    project: "samples/Recovery/Trax.Samples.Recovery.Api",
    port: 5392,
    database: "trax_dashboard_recording_recovery",
    key: "recovery-operator-key-do-not-use-in-production",
    // The recorder sends the dashboard's documents and the sample's own mutations as text, which the host refuses
    // unless it is told to take documents it has not registered.
    env: { Recovery__RequirePersistedOperations: "false" },
  },
  persisted: {
    alias: "persisted-operations",
    project: "samples/PersistedOperations/Trax.Samples.PersistedOperations.Api",
    port: 5393,
    database: "trax_dashboard_recording_persisted_operations",
    key: "operator-key-do-not-use-in-production",
    env: {},
  },
};
const PRIMARY = "scheduling";
const SCHEDULER_HOSTS = ["scheduling", "recovery"];
// The hosts that register state machines.
const MACHINE_HOSTS = ["recovery"];

// Page sizes and ranges the console asks for, where the recorder reads a page part by part (see TAKES for lists).
const MACHINE_PAGE = 20; // pages/MachinePage.tsx
const TRANSITIONS_PAGE = 25; // components/TransitionHistory.tsx
const BULK_FAILURES_PAGE = 50; // pages/BulkOperationsPage.tsx
const SCHEDULES_COUNT = 3; // pages/ManifestsPage.tsx, next runs per row
const DEAD_LETTER_GROUP_WINDOW = 500; // pages/DeadLettersPage.tsx
const GROUP_STATUS_IDS = 1000; // lib/schedules.ts
const SERIES_RANGES = ["LAST24_HOURS", "LAST7_DAYS"];
// The train page's ranges: the failed runs it places on steps start this long before the minute it loaded.
const TRAIN_RANGE_MS = { LAST24_HOURS: 24 * 3600_000, LAST7_DAYS: 7 * 24 * 3600_000 };

// ── The dashboard's documents ────────────────────────────────────────────────

/** Every operation document in the dashboard's src/graphql, by operation name, with `${CONST}` fragments filled in. */
function readDocuments() {
  const docs = {};
  const consts = {};
  for (const file of ["queries.ts", "mutations.ts", "subscriptions.ts"]) {
    const src = readFileSync(join(dashboardDir, "src/graphql", file), "utf8");
    for (const m of src.matchAll(/const (\w+) = `([\s\S]*?)`;/g)) consts[m[1]] = m[2];
    for (const m of src.matchAll(/export const \w+ = gql`([\s\S]*?)`;/g)) {
      const text = m[1].replace(/\$\{(\w+)\}/g, (_, name) => {
        if (!(name in consts)) throw new Error(`${file}: unknown fragment constant ${name}`);
        return consts[name];
      });
      const name = /(?:query|mutation|subscription)\s+(\w+)/.exec(text)?.[1];
      if (!name) throw new Error(`${file}: a document without an operation name`);
      docs[name] = text.trim();
    }
  }
  return docs;
}

// ── Hosts ────────────────────────────────────────────────────────────────────

function psql(sql) {
  const [command, ...rest] = PSQL;
  execFileSync(command, [...rest, "-q", "-U", "trax", "-d", "trax", "-c", sql], {
    stdio: ["ignore", "ignore", "inherit"],
    env: { ...process.env, PGPASSWORD: process.env.PGPASSWORD ?? "trax123" },
  });
}

function connectionString(database) {
  return `Host=${PG_HOST};Port=${PG_PORT};Database=${database};Username=trax;Password=trax123`;
}

const children = [];

/** Refuses to start when something already listens on a host's port, which would answer in its place. */
function ensurePortFree(port) {
  return new Promise((resolvePort, reject) => {
    const probe = createServer()
      .once("error", () => reject(new Error(`port ${port} is in use; stop whatever listens there first`)))
      .once("listening", () => probe.close(() => resolvePort()))
      .listen(port, "127.0.0.1");
  });
}

function startHost(name) {
  const host = HOSTS[name];
  psql(`DROP DATABASE IF EXISTS ${host.database} WITH (FORCE)`);
  psql(`CREATE DATABASE ${host.database}`);
  const logFile = openSync(join(out, `${name}.log`), "w");
  const child = spawn(
    "dotnet",
    ["run", "--no-build", "--no-launch-profile", "--project", join(repo, host.project)],
    {
      cwd: repo,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        DOTNET_ENVIRONMENT: "Development",
        Kestrel__Endpoints__Http__Url: `http://localhost:${host.port}`,
        ConnectionStrings__TraxDatabase: connectionString(host.database),
        ...host.env,
      },
      stdio: ["ignore", logFile, logFile],
      // Its own process group, so stopping it stops the host `dotnet run` started too.
      detached: true,
    },
  );
  closeSync(logFile);
  child.on("exit", (code) => {
    if (!stopping) console.error(`${name} host exited (${code}); see out/dashboard/${name}.log`);
  });
  children.push(child);
}

let stopping = false;
function stopHosts() {
  stopping = true;
  for (const child of children) {
    try {
      process.kill(-child.pid, "SIGINT");
    } catch {
      /* already gone */
    }
  }
}
process.on("exit", stopHosts);
process.on("SIGINT", () => process.exit(130));

// Ready means its database is migrated: the probe reads from it.
async function waitReady(name, probe, variables) {
  for (let i = 0; i < 240; i++) {
    try {
      await call(name, probe, variables);
      return;
    } catch {
      await sleep(500);
    }
  }
  throw new Error(`${name} did not start; see out/dashboard/${name}.log`);
}

// ── GraphQL ──────────────────────────────────────────────────────────────────

let DOCS = {};

async function post(name, query, variables, operationName) {
  const host = HOSTS[name];
  const response = await fetch(`http://localhost:${host.port}/trax/graphql`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": host.key },
    body: JSON.stringify({ query, variables, operationName }),
  });
  return response.json();
}

/** Sends one of the dashboard's documents and returns its data; an error is thrown. */
async function call(name, op, variables = {}) {
  if (!DOCS[op]) throw new Error(`the dashboard has no operation ${op}`);
  const body = await post(name, DOCS[op], variables, op);
  if (body.errors?.length) throw new Error(`${name} ${op}: ${body.errors[0].message}`);
  return body.data;
}

/** Sends a driving request that is not the dashboard's (the samples' own mutations). */
async function drive(name, query, variables = {}) {
  const body = await post(name, query, variables);
  if (body.errors?.length) throw new Error(`${name}: ${body.errors[0].message} (${query.slice(0, 60)}...)`);
  return body.data;
}

const raw = {
  recordedAt: null,
  home: homedir(), // the recording machine's home directory, which a host's log can name
  hosts: {},
  queries: [], // { host, op, vars, data }
  walks: [], // { host, op, vars, items, envelope }
  mutations: [], // { host, op, vars, response, phase }
  frames: [], // { host, op, vars, t, data }
  attentionBeforeFreeze: {}, // host -> Attention, read while the schedulers still ran
};

/** Sends one of the dashboard's mutations and records what the host answered, errors and refusals included. */
async function mutate(name, op, vars, phase = "mutations") {
  const response = await post(name, DOCS[op], vars, op);
  raw.mutations.push({ host: name, op, vars, response, phase });
  const summary = JSON.stringify(response).slice(0, 160);
  log(`  ${name} ${op} ${JSON.stringify(vars).slice(0, 80)} -> ${summary}`);
  return response;
}

async function record(name, op, vars = {}) {
  const data = await call(name, op, vars);
  raw.queries.push({ host: name, op, vars, data });
  return data;
}

// Where each paged list keeps its page in the response.
const PAGES = {
  Executions: (d) => d.operations.executions,
  ExecutionChildren: (d) => d.operations.executionChildren,
  WorkQueue: (d) => d.operations.workQueue.workQueues,
  DeadLetters: (d) => d.operations.deadLetters.deadLetters,
  Logs: (d) => d.operations.logs.logs,
  Manifests: (d) => d.operations.manifests,
  ManifestSchedules: (d) => d.operations.manifests,
  ManifestGroups: (d) => d.operations.manifestGroups.groups,
};
const PAGE_PATHS = {
  Executions: ["operations", "executions"],
  ExecutionChildren: ["operations", "executionChildren"],
  WorkQueue: ["operations", "workQueue", "workQueues"],
  DeadLetters: ["operations", "deadLetters", "deadLetters"],
  Logs: ["operations", "logs", "logs"],
  Manifests: ["operations", "manifests"],
  ManifestSchedules: ["operations", "manifests"],
  ManifestGroups: ["operations", "manifestGroups", "groups"],
};
const WALK_TAKE = 100;

/** Reads a list to its end with the given filter, and records every row and the page's other fields. */
async function walk(name, op, vars = {}, { scope = "merge" } = {}) {
  const items = [];
  let envelope = null;
  let afterId = null;
  for (let i = 0; i < 1000; i++) {
    const page = PAGES[op](await call(name, op, { ...vars, take: WALK_TAKE, afterId }));
    if (!envelope) {
      const { items: _items, nextCursor: _cursor, ...rest } = page;
      envelope = rest;
    }
    items.push(...page.items);
    if (page.items.length < WALK_TAKE || page.nextCursor == null) break;
    afterId = page.nextCursor;
  }
  raw.walks.push({ host: name, op, vars, scope, items, envelope });
  return items;
}

// ── Drive ────────────────────────────────────────────────────────────────────

const RECOVERY = {
  startRun: `mutation ($input: StartRunInput!) { dispatch { startRun(input: $input) { output {
    runId manifestId manifestExternalId trainName armedCrash maxRetries } } } }`,
  changeCaseData: `mutation ($runId: String!) { dispatch { changeCaseData(input: { runId: $runId }) { output { change } } } }`,
  runs: `query ($manifestId: Long!) { operations { executions(manifestId: $manifestId, order: OLDEST, take: 20) {
    items { id trainState } } } }`,
};
const ENDED = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

/** Subscribes the way the dashboard does and writes down every frame with the time it arrived. */
function listen(ws, name, op, vars = {}) {
  return ws.subscribe(
    { query: DOCS[op], variables: vars, operationName: op },
    {
      next: ({ data }) => data && raw.frames.push({ host: name, op, vars, t: Date.now(), data }),
      error: (e) => console.error(`${name} ${op}:`, e),
      complete: () => {},
    },
  );
}

/** A Recovery run, from start to the end of its last attempt; `fork` acts during the backoff as the sample's page does. */
async function recoveryRun(ws, input, fork) {
  const { dispatch } = await drive("recovery", RECOVERY.startRun, { input });
  const run = dispatch.startRun.output;
  log(`recovery run ${run.runId} (${input.scenario}, crash ${input.crashOnce}, ${fork})`);
  const followed = new Set();
  let forked = false;
  for (let i = 0; i < 400; i++) {
    const { operations } = await drive("recovery", RECOVERY.runs, { manifestId: run.manifestId });
    const rows = operations.executions.items;
    for (const row of rows)
      if (!followed.has(row.id)) {
        followed.add(row.id);
        listen(ws, "recovery", "OnJunctionEvent", { metadataId: row.id });
      }
    const last = rows[rows.length - 1];
    if (!forked && rows.length === 1 && last.trainState === "FAILED") {
      forked = true;
      if (fork === "askAfresh")
        await mutate("recovery", "TriggerManifest", { externalId: run.manifestExternalId, askAfresh: true }, "drive");
      if (fork === "changeData") await drive("recovery", RECOVERY.changeCaseData, { runId: run.runId });
    }
    if (last?.trainState === "COMPLETED") return { run, last: last.id };
    await sleep(250);
  }
  throw new Error(`recovery run ${run.runId} did not finish`);
}

async function waitFor(what, check, timeoutMs = 120_000) {
  const until = Date.now() + timeoutMs;
  let last = null;
  while (Date.now() < until) {
    // A host still migrating its database answers with an error; that is "not yet".
    const value = await check().catch((e) => ((last = e), null));
    if (value) return value;
    await sleep(500);
  }
  if (last) console.error(last);
  throw new Error(`timed out waiting for ${what}`);
}

const awaitingDeadLetters = async () =>
  (await call("scheduling", "DeadLetters", { take: 50, status: "AWAITING_INTERVENTION" })).operations.deadLetters
    .deadLetters.items;

async function driveScheduling() {
  // The cron digest never comes due in a recording; trigger it once, as an operator would.
  await waitFor("the scheduler to seed its manifests", async () => {
    const r = await call("scheduling", "Manifests", { take: 50 });
    return r.operations.manifests.items.length >= 6;
  });
  await mutate("scheduling", "TriggerManifest", { externalId: "send-daily-digest", askAfresh: false }, "drive");

  // Three rounds of the outage: requeue the first dead letter, acknowledge the second, leave the third.
  const first = await waitFor("the first dead letter", async () => (await awaitingDeadLetters())[0]);
  log(`dead letter ${first.id}: requeue`);
  await mutate("scheduling", "RequeueDeadLetter", { id: first.id, askAfresh: false }, "drive");
  const second = await waitFor("the second dead letter", async () =>
    (await awaitingDeadLetters()).find((d) => d.id !== first.id),
  );
  log(`dead letter ${second.id}: acknowledge`);
  await mutate(
    "scheduling",
    "AcknowledgeDeadLetter",
    { id: second.id, note: "Supplier confirmed the outage; their next feed covers it." },
    "drive",
  );
  // An acknowledged manifest waits for its next due run; the third round dead-letters it again.
  const third = await waitFor(
    "the third dead letter",
    async () => (await awaitingDeadLetters()).find((d) => d.id !== first.id && d.id !== second.id),
    180_000,
  );
  log(`dead letter ${third.id}: left awaiting`);
}

async function drivePersistedOperations() {
  const manifest = JSON.parse(
    readFileSync(join(repo, "samples/PersistedOperations/Trax.Samples.PersistedOperations.Client/manifest.json"), "utf8"),
  ).operations;
  const upload = (input) => mutate("persisted", "UploadPersistedOperation", { input }, "drive");
  const greet = manifest.find((o) => o.id === "greet_v1");
  const formal = greet.document.replace("style: CASUAL", "style: FORMAL");
  await upload({ id: greet.id, document: greet.document, description: "Greets a visitor by name." });
  await sleep(1500);
  // The client's fix: a formal greeting, the same shape, so the store takes it and keeps the history.
  await upload({ id: greet.id, document: formal, description: "Greets a visitor by name, formally." });
  // An edit that renames the field the app reads, which the guardrail refuses.
  await upload({ id: greet.id, document: formal.replace("{ greeting }", "{ message: greeting }") });
  // A tenant's own copy, and a retired v0 kept for its history.
  await upload({ id: greet.id, document: greet.document, tenantKey: "acme", description: "Acme's greeting." });
  await upload({ id: "greet_v0", document: greet.document, description: "The first greeting." });
  await sleep(1000);
  await mutate(
    "persisted",
    "DeactivatePersistedOperation",
    { input: { id: "greet_v0", reason: "Every client sends greet_v1 now." } },
    "drive",
  );
}

// The Recovery sample's machines, driven the way its page and an operator do: discovery starts one system-owned
// instance per index partition (one of them ends up held for review), and the operator's own orders and topic map
// drafts are user-owned. Every answer type here is the sample's, not the dashboard's.
const MACHINE_DRIVE = {
  discover: `mutation { dispatch { discoverPartitions(input: {}) { output { instancesStarted instancesSentToIngest } } } }`,
  orderTrouble: `mutation ($trouble: OrderTrouble!) { dispatch { orderTrouble(input: { trouble: $trouble }) { output { trouble } } } }`,
  orderStart: `mutation ($requestId: String!) { machines { order { start(requestId: $requestId) { id state problem { code message } } } } }`,
  orderAdd: `mutation ($id: UUID!, $input: NewItemInput!, $requestId: String!) { machines { order {
    addItem(id: $id, input: $input, requestId: $requestId) { id state problem { code message } } } } }`,
  orderPlace: `mutation ($id: UUID!, $requestId: String!) { machines { order {
    placeOrder(id: $id, requestId: $requestId) { id state problem { code message } } } } }`,
  orderGet: `query ($id: UUID!) { machines { order { get(id: $id) { state } } } }`,
  mapStart: `mutation ($requestId: String!) { machines { topicMap { start(requestId: $requestId) { id state problem { code message } } } } }`,
  mapFields: `mutation ($id: UUID!, $input: FieldsChoiceInput!, $requestId: String!) { machines { topicMap {
    chooseFields(id: $id, input: $input, requestId: $requestId) { id state problem { code message } } } } }`,
  mapBuild: `mutation ($id: UUID!, $input: RangeChoiceInput!, $requestId: String!) { machines { topicMap {
    build(id: $id, input: $input, requestId: $requestId) { id state problem { code message } } } } }`,
  mapGet: `query ($id: UUID!) { machines { topicMap { get(id: $id) { state } } } }`,
};
let requests = 0;
const requestId = (what) => `dashboard-recording-${what}-${++requests}`;

function answered(what, answer) {
  if (answer.problem) throw new Error(`${what} was refused: ${JSON.stringify(answer.problem)}`);
  return answer;
}

async function driveMachines() {
  const r = "recovery";
  const found = (await drive(r, MACHINE_DRIVE.discover)).dispatch.discoverPartitions.output;
  log(`recovery partitions: ${found.instancesStarted} started, ${found.instancesSentToIngest} sent to ingest`);

  const order = async (trouble, end) => {
    if (trouble) await drive(r, MACHINE_DRIVE.orderTrouble, { trouble });
    const { id } = answered("order start", (await drive(r, MACHINE_DRIVE.orderStart, { requestId: requestId("start") })).machines.order.start);
    answered(
      "addItem",
      (await drive(r, MACHINE_DRIVE.orderAdd, { id, input: { item: "Margherita" }, requestId: requestId("add") })).machines.order.addItem,
    );
    if (!end) return log(`order ${id} left in its cart`);
    answered("placeOrder", (await drive(r, MACHINE_DRIVE.orderPlace, { id, requestId: requestId("place") })).machines.order.placeOrder);
    await waitFor(`order ${id} to reach ${end}`, async () => (await drive(r, MACHINE_DRIVE.orderGet, { id })).machines.order.get?.state === end);
    log(`order ${id} ${end}`);
  };
  await order("NONE", "Delivered");
  await order("RESTAURANT_CLOSED", "Cancelled");
  // Its timer clears the cart half an hour after the snapshot, so the instance page shows when.
  await order(null, null);

  const map = async (build) => {
    const { id } = answered("topic map start", (await drive(r, MACHINE_DRIVE.mapStart, { requestId: requestId("map") })).machines.topicMap.start);
    answered(
      "chooseFields",
      (await drive(r, MACHINE_DRIVE.mapFields, { id, input: { fields: ["Hydrology"] }, requestId: requestId("fields") })).machines.topicMap
        .chooseFields,
    );
    if (!build) return log(`topic map ${id} left choosing its years`);
    answered(
      "build",
      (await drive(r, MACHINE_DRIVE.mapBuild, { id, input: { fromYear: 2016, toYear: 2025 }, requestId: requestId("build") })).machines
        .topicMap.build,
    );
    const state = await waitFor(`topic map ${id} to build`, async () => {
      const s = (await drive(r, MACHINE_DRIVE.mapGet, { id })).machines.topicMap.get?.state;
      return s && s !== "Building" ? s : null;
    });
    log(`topic map ${id} ${state}`);
  };
  await map(true);
  await map(false);

  await waitFor("every partition to finish ingesting", async () => {
    const page = (await call(r, "MachineInstancesAttention", { machine: "source-partition", skip: 0, take: 100 })).operations
      .machineInstances;
    return page.items.length > 0 && page.items.every((i) => !["Discovered", "Ingesting"].includes(i.state));
  });
  log("recovery machines done");
}

async function driveAll() {
  const ws = {};
  for (const name of SCHEDULER_HOSTS) {
    const host = HOSTS[name];
    ws[name] = createClient({
      url: `ws://localhost:${host.port}/trax/graphql`,
      connectionParams: { apiKey: host.key },
    });
    listen(ws[name], name, "OnTrainStateChanged");
    listen(ws[name], name, "OnDataChanged");
  }

  const recovery = (async () => {
    // Two at a time: a crashing run waits out its backoff, so they overlap the way people use the page.
    const pairs = [
      [
        [{ scenario: "REFUND", orderId: "A-1001", crashOnce: true, crashWhile: "EMAILING" }, "none"],
        [{ scenario: "REFUND", orderId: "A-1001", crashOnce: true }, "changeData"],
      ],
      [
        [{ scenario: "REFUND", orderId: "A-1002", crashOnce: true }, "askAfresh"],
        [{ scenario: "REFUND", orderId: "A-1003", crashOnce: false }, "none"],
      ],
    ];
    const finished = [];
    for (const pair of pairs)
      finished.push(...(await Promise.all(pair.map(([input, fork]) => recoveryRun(ws.recovery, input, fork)))));
    // Requeue the first run's last attempt without asking afresh: the new run replays its decisions.
    const source = finished[0].last;
    const requeued = await mutate("recovery", "RequeueExecution", { id: source, askAfresh: false }, "drive");
    const entry = requeued.data.operations.requeueExecution.id;
    const metadataId = await waitFor("the requeued run to start", async () => {
      const d = await call("recovery", "WorkQueueDetail", { id: entry });
      return d.operations.workQueue.detail?.metadataId;
    });
    listen(ws.recovery, "recovery", "OnJunctionEvent", { metadataId });
    await waitFor("the requeued run to finish", async () => {
      const d = await call("recovery", "ExecutionDetail", { id: metadataId });
      return ENDED.has(d.operations.executionDetail.trainState);
    });
    log("recovery runs done");
  })();

  await Promise.all([driveScheduling(), recovery, driveMachines(), drivePersistedOperations()]);
  // A last stretch of ordinary traffic, so the live feed has a steady minute to replay.
  log("recording a minute of steady traffic");
  await sleep(60_000);
  return ws;
}

// ── Freeze ───────────────────────────────────────────────────────────────────

async function freeze() {
  for (const name of SCHEDULER_HOSTS) await record(name, "SchedulerConfig");
  // What needs attention says whether the schedulers run; the demo shows them running, as the settings do.
  for (const name of SCHEDULER_HOSTS) raw.attentionBeforeFreeze[name] = await call(name, "Attention");
  for (const name of SCHEDULER_HOSTS)
    await mutate(name, "UpdateScheduler", { input: { manifestManagerEnabled: false, jobDispatcherEnabled: false } }, "freeze");
  // Let what was running finish, then wait for the cleanup and pollers to have nothing left to write.
  for (const name of SCHEDULER_HOSTS)
    await waitFor(`${name} to settle`, async () => {
      const d = await call(name, "Executions", { take: 50, trainState: "IN_PROGRESS" });
      const p = await call(name, "Executions", { take: 50, trainState: "PENDING" });
      return d.operations.executions.items.length === 0 && p.operations.executions.items.length === 0;
    });
  await sleep(3000);
  // Two entries that will now wait in the queue, and one cancelled.
  await mutate("scheduling", "QueueTrain", {
    input: {
      trainName: "Trax.Samples.Scheduling.Trains.ImportSupplierFeed.IImportSupplierFeedTrain",
      inputJson: JSON.stringify({ supplier: "globex" }),
      priority: 5,
    },
  }, "freeze");
  await mutate("scheduling", "QueueTrain", {
    input: {
      trainName: "Trax.Samples.Scheduling.Trains.SendDailyDigest.ISendDailyDigestTrain",
      inputJson: JSON.stringify({ audience: "staff" }),
      priority: 0,
    },
  }, "freeze");
  const cancelled = await mutate("scheduling", "QueueTrain", {
    input: {
      trainName: "Trax.Samples.Scheduling.Trains.SendDailyDigest.ISendDailyDigestTrain",
      inputJson: JSON.stringify({ audience: "everyone" }),
      priority: 0,
    },
  }, "freeze");
  await mutate("scheduling", "CancelWorkQueueEntry", { id: cancelled.data.operations.workQueue.queueTrain.id }, "freeze");

  // Two bulk operations an operator ran from the schedules list, which leave the manifests as they were: every
  // manifest of the refresh train disabled, then enabled again. A host knows only the operations it ran.
  const filter = { manifests: { nameContains: "Refresh", hideAdminTrains: true } };
  const matching = (await call("scheduling", "Manifests", { take: 100, ...filter.manifests })).operations.manifests.items.length;
  for (const kind of ["DISABLE", "ENABLE"]) {
    const started = await mutate("scheduling", "StartBulkOperation", { kind, filter, expectedCount: matching, askAfresh: false }, "freeze");
    const id = started.data?.operations?.startBulkOperation?.operation?.id;
    if (!id) throw new Error(`the bulk ${kind} did not start: ${JSON.stringify(started)}`);
    await waitFor(`the bulk ${kind} to finish`, async () => {
      const op = (await call("scheduling", "BulkOperation", { id, failuresTake: BULK_FAILURES_PAGE })).operations.bulkOperation;
      return op && op.status !== "RUNNING";
    });
  }
}

// ── Snapshot ─────────────────────────────────────────────────────────────────

const TRAIN_STATES = ["PENDING", "IN_PROGRESS", "COMPLETED", "FAILED", "CANCELLED"];
const FAILURE_CLASSES = ["TRANSIENT", "CONFLICT", "PERMANENT", "UNCLASSIFIED"];
const SCHEDULE_TYPES = ["NONE", "CRON", "INTERVAL", "ON_DEMAND", "DEPENDENT", "DORMANT_DEPENDENT", "ONCE"];
const LOG_LEVELS = ["TRACE", "DEBUG", "INFORMATION", "WARNING", "ERROR", "CRITICAL"];

/** The words a person would type into a text filter, chosen from what the hosts hold. */
const PRESETS = {
  manifestName: ["Refresh", "Supplier", "Refund"],
  groupName: ["refresh", "supplier", "recovery"],
  logCategory: ["Trax.Samples", "Junction"],
  logMessage: ["Imported", "503", "digest"],
};

async function snapshot() {
  const hostsByName = {};
  for (const name of SCHEDULER_HOSTS) {
    const hosts = (await record(name, "Hosts")).operations.hosts;
    hostsByName[name] = hosts;
  }
  raw.hosts = Object.fromEntries(
    Object.entries(hostsByName).map(([name, hosts]) => [
      name,
      { machine: hosts[0]?.name ?? hostname(), instanceIds: hosts.map((h) => h.instanceId) },
    ]),
  );

  // Process state, and the merged singletons.
  for (const name of SCHEDULER_HOSTS) {
    for (const hideAdmin of [true, false]) {
      for (const range of ["LAST24_HOURS", "LAST60_MINUTES"]) await record(name, "Overview", { range, hideAdmin });
      await record(name, "Trains", { hideAdmin });
    }
    for (const op of ["EnvironmentName", "ServerVersion", "LogLevels", "Effects", "AdminTrainNames", "GroupDependencyGraph"])
      await record(name, op);
    if (name !== PRIMARY) await record(name, "SchedulerConfig");
    else await record(name, "ServerProcess");
    await record(name, "Attention");
    await record(name, "TrainsAskingDecisions");
    await record(name, "TrainChains", { hideAdmin: false });
    // The bulk operations this host ran, each with its failures page by page.
    for (const op of (await record(name, "BulkOperations")).operations.bulkOperations) {
      let failuresAfter = null;
      for (let i = 0; i < 100; i++) {
        const detail = (await record(name, "BulkOperation", { id: op.id, failuresAfter, failuresTake: BULK_FAILURES_PAGE })).operations
          .bulkOperation;
        const failures = detail?.failures ?? [];
        if (failures.length < BULK_FAILURES_PAGE) break;
        failuresAfter = failures[failures.length - 1].id;
      }
    }
  }
  await record("persisted", "PersistedOperationsAvailable");

  // The whole of each list, which every renumbering starts from.
  const all = {};
  for (const name of SCHEDULER_HOSTS) {
    all[name] = {
      executions: await walk(name, "Executions", { order: "OLDEST", hideAdminTrains: false }, { scope: "base" }),
      manifests: await walk(name, "Manifests", { hideAdminTrains: false }, { scope: "base" }),
      groups: await walk(name, "ManifestGroups", {}, { scope: "base" }),
      workQueue: await walk(name, "WorkQueue", {}, { scope: "base" }),
      deadLetters: await walk(name, "DeadLetters", {}, { scope: "base" }),
      logs: await walk(name, "Logs", { order: "OLDEST" }, { scope: "base" }),
    };
    log(
      `${name}: ${Object.entries(all[name])
        .map(([k, v]) => `${v.length} ${k}`)
        .join(", ")}`,
    );
  }

  const adminNames = new Set();
  for (const name of SCHEDULER_HOSTS)
    for (const n of (await call(name, "AdminTrainNames")).operations.adminTrainNames) adminNames.add(n);

  // Every list, once per control. "merge" lists are answered by both hosts and combined; "owner" lists are
  // scoped to one host's row and answered by that host alone; "host" lists name the host itself.
  for (const name of SCHEDULER_HOSTS) {
    const a = all[name];
    const trainNames = [...new Set(a.executions.map((e) => e.name))];
    const userTrains = trainNames.filter((n) => !adminNames.has(n));

    // The Executions page, with admin trains hidden (the default) and shown (User settings).
    for (const hideAdminTrains of [true, false]) {
      for (const order of ["NEWEST", "OLDEST"]) await walk(name, "Executions", { order, hideAdminTrains });
      const filtered = { order: "NEWEST", hideAdminTrains };
      for (const trainState of TRAIN_STATES) await walk(name, "Executions", { ...filtered, trainState });
      for (const failureClass of FAILURE_CLASSES) await walk(name, "Executions", { ...filtered, failureClass });
      for (const trainName of hideAdminTrains ? userTrains : trainNames)
        await walk(name, "Executions", { ...filtered, trainName });
      await walk(name, "Executions", { ...filtered, hostName: raw.hosts[name].machine }, { scope: "host" });
    }
    const pageBase = { order: "NEWEST", hideAdminTrains: true };
    // A train's page, for every train the host registers, run or not.
    const registered = (await call(name, "Trains", { hideAdmin: false })).operations.trains.map((t) => t.fullName);
    for (const trainName of new Set([...trainNames, ...registered]))
      await walk(name, "Executions", { trainName, order: "NEWEST" });
    // One run looked up by its external id, per host: the latest of the trains people run.
    const latest = [...a.executions].reverse().find((e) => !adminNames.has(e.name));
    if (latest) await walk(name, "Executions", { ...pageBase, externalId: latest.externalId }, { scope: "owner" });
    const parents = new Set(a.executions.map((e) => e.parentId).filter((p) => p != null));
    for (const parentId of parents) {
      await walk(name, "Executions", { ...pageBase, parentId }, { scope: "owner" });
      await walk(name, "ExecutionChildren", { parentId }, { scope: "owner" });
    }
    for (const m of a.manifests) {
      await walk(name, "Executions", { manifestId: m.id }, { scope: "owner" });
      await walk(name, "Executions", { manifestId: m.id, trainState: "FAILED" }, { scope: "owner" });
      await walk(name, "WorkQueue", { manifestId: m.id }, { scope: "owner" });
      await walk(name, "DeadLetters", { manifestId: m.id }, { scope: "owner" });
    }
    for (const g of a.groups) {
      await walk(name, "Executions", { manifestGroupId: g.id }, { scope: "owner" });
      await walk(name, "Manifests", { manifestGroupId: g.id }, { scope: "owner" });
    }

    await walk(name, "WorkQueue", {});
    for (const status of ["QUEUED", "DISPATCHED", "CANCELLED"]) await walk(name, "WorkQueue", { status });
    for (const trainName of userTrains) await walk(name, "WorkQueue", { trainName });
    const subjects = new Set(a.workQueue.map((w) => w.subjectKey).filter(Boolean));
    for (const subjectKey of subjects) await walk(name, "WorkQueue", { subjectKey });

    await walk(name, "DeadLetters", {});
    for (const status of ["AWAITING_INTERVENTION", "RETRIED", "ACKNOWLEDGED"]) await walk(name, "DeadLetters", { status });

    // The schedules list, and its rows' schedules in words and next runs, read for the same filters.
    for (const op of ["Manifests", "ManifestSchedules"]) {
      const extra = op === "ManifestSchedules" ? { count: SCHEDULES_COUNT } : {};
      for (const hideAdminTrains of [true, false]) {
        await walk(name, op, { hideAdminTrains, ...extra });
        for (const isEnabled of [true, false]) await walk(name, op, { isEnabled, hideAdminTrains, ...extra });
        for (const scheduleType of SCHEDULE_TYPES) await walk(name, op, { scheduleType, hideAdminTrains, ...extra });
        for (const nameContains of PRESETS.manifestName) await walk(name, op, { nameContains, hideAdminTrains, ...extra });
      }
    }

    await walk(name, "ManifestGroups", {});
    for (const nameContains of PRESETS.groupName) await walk(name, "ManifestGroups", { nameContains });

    for (const order of ["NEWEST", "OLDEST"]) await walk(name, "Logs", { order });
    for (const minimumLevel of LOG_LEVELS) await walk(name, "Logs", { order: "NEWEST", minimumLevel });
    for (const categoryContains of PRESETS.logCategory) await walk(name, "Logs", { order: "NEWEST", categoryContains });
    for (const messageContains of PRESETS.logMessage) await walk(name, "Logs", { order: "NEWEST", messageContains });

    // Every run: its page, timeline, decisions, log (the run's own, oldest first, and the Logs page's run filter).
    for (const e of a.executions) {
      const detail = await record(name, "ExecutionDetail", { id: e.id });
      await record(name, "RunGraph", { metadataId: e.id });
      await record(name, "RunLinks", { id: e.id });
      await walk(name, "Logs", { metadataId: e.id, order: "OLDEST" }, { scope: "owner" });
      await walk(name, "Logs", { metadataId: e.id, order: "NEWEST" }, { scope: "owner" });
      await record(name, "JunctionRuns", { metadataId: e.id, take: 500 });
      let afterId = null;
      for (let i = 0; i < 20; i++) {
        const page = (await record(name, "Decisions", { metadataId: e.id, afterId, take: 26 })).operations.decisions;
        if (page.items.length < 26 || page.nextCursor == null) break;
        afterId = page.items[24].id;
      }
      if (detail.operations.executionDetail?.childCount > 0)
        await walk(name, "ExecutionChildren", { parentId: e.id }, { scope: "owner" });
    }
    for (const w of a.workQueue) await record(name, "WorkQueueDetail", { id: w.id });
    for (const d of a.deadLetters) await record(name, "DeadLetterDetail", { id: d.id });
    for (const m of a.manifests) {
      await record(name, "ManifestDetail", { id: m.id });
      await record(name, "ManifestStats", { manifestId: m.id });
      await record(name, "ManifestExclusions", { manifestId: m.id });
      // The manifest page's schedule, and a train page's one-line row for it.
      for (const count of [5, 1]) await record(name, "ManifestSchedule", { id: m.id, count });
      for (const range of SERIES_RANGES) await record(name, "MetricSeries", { scope: "MANIFEST", range, id: m.id });
    }
    for (const g of a.groups) {
      const detail = await record(name, "ManifestGroupDetail", { id: g.id });
      await record(name, "ManifestGroupStats", { groupIds: [g.id] });
      for (const range of SERIES_RANGES) await record(name, "MetricSeries", { scope: "GROUP", range, id: g.id });
      const nodes = detail.operations.manifestGroups.graph?.nodes ?? [];
      if (nodes.length) await record(name, "GroupGraphStatus", { groupIds: nodes.slice(0, GROUP_STATUS_IDS).map((n) => n.id) });
    }
    const graph = (await call(name, "GroupDependencyGraph")).operations.manifestGroups.dependencyGraph;
    if (graph.nodes.length)
      await record(name, "GroupGraphStatus", { groupIds: graph.nodes.slice(0, GROUP_STATUS_IDS).map((n) => n.id) });
    for (const trainName of new Set([...trainNames, ...registered])) {
      await record(name, "TrainStats", { trainName });
      // A train's page: its runs over each range, the failed runs it places on the chain's steps, and its manifests.
      for (const range of SERIES_RANGES) {
        await record(name, "MetricSeries", { scope: "TRAIN", range, name: trainName });
        const startedAfter = new Date(Date.parse(raw.recordedAt) - TRAIN_RANGE_MS[range]).toISOString();
        await walk(name, "Executions", { trainName, trainState: "FAILED", startedAfter, order: "NEWEST" });
      }
      await walk(name, "Manifests", { nameContains: trainName, hideAdminTrains: false });
      // The Overview's failing-train link: the Runs page filtered to the train's failed runs.
      for (const hideAdminTrains of [true, false])
        await walk(name, "Executions", { order: "NEWEST", hideAdminTrains, trainState: "FAILED", trainName });
    }
    log(`${name}: snapshot read`);
  }

  // The state machines: every machine, its counts, its instances under every filter its page offers, and every
  // instance's page and history. A user's draft is named by its row too; nothing here reads a context or an owner.
  for (const name of MACHINE_HOSTS) {
    const machines = (await record(name, "Machines")).operations.machines;
    await record(name, "MachineInstanceStuckCounts", {});
    const instances = new Map();
    for (const m of machines) {
      const counts = (await record(name, "MachineInstanceStuckCounts", { machine: m.name })).operations.machineInstanceCounts;
      const states = [null, ...new Set([...m.states.map((s) => s.name), ...counts.map((c) => c.state)])];
      for (const state of states)
        for (const ownerKind of [null, "SYSTEM", "USER"])
          for (const stuck of [null, true])
            for (let skip = 0; ; skip += MACHINE_PAGE) {
              const page = (
                await record(name, "MachineInstancesAttention", { machine: m.name, state, ownerKind, stuck, skip, take: MACHINE_PAGE })
              ).operations.machineInstances;
              for (const i of page.items) instances.set(`${i.machine}\u0000${i.ownerKind}\u0000${i.id}\u0000${i.rowId}`, i);
              if (skip + MACHINE_PAGE >= page.totalCount) break;
            }
    }
    for (const i of instances.values()) {
      // The console names a system instance by its id alone, and a user's draft by its row as well.
      const key = { machine: i.machine, ownerKind: i.ownerKind, id: i.id, rowId: i.ownerKind === "USER" ? i.rowId : null };
      const instance = (await record(name, "MachineInstance", key)).operations.machineInstance;
      // The history names the instance by the row the instance page read, a system instance's too.
      const history = { ...key, rowId: instance?.rowId ?? key.rowId };
      let after = null;
      for (let n = 0; n < 100; n++) {
        const page = (await record(name, "MachineInstanceTransitions", { ...history, first: TRANSITIONS_PAGE, after })).operations
          .machineInstance?.transitions;
        if (!page || page.nextCursor == null) break;
        after = page.nextCursor;
      }
    }
    log(`${name}: ${machines.length} machines, ${instances.size} instances`);
  }

  // The persisted-operations store, under every filter the page offers.
  const listed = new Map();
  for (const isActive of [null, true, false])
    for (const tenantKey of [null, "", "acme"])
      for (const idStartsWith of [null, "greet", "lookup"])
        for (let skip = 0; ; skip += 25) {
          const page = (
            await record("persisted", "PersistedOperations", { filter: { isActive, tenantKey, idStartsWith }, skip, take: 25 })
          ).persistedOperations.persistedOperations;
          for (const op of page.items) listed.set(`${op.tenantKey}\u0000${op.id}`, op);
          if (skip + 25 >= page.totalCount) break;
        }
  for (const op of listed.values())
    // The page reads the tenant from its URL and sends the default tenant ("") as none.
    await record("persisted", "PersistedOperationDetail", { id: op.id, tenantKey: op.tenantKey || null, historyTake: 100 });
  log(`persisted operations: ${listed.size} in the store`);
}

// ── Mutations ────────────────────────────────────────────────────────────────

/** Every write the dashboard offers, sent to a real host so the demo answers with Trax's own words. */
async function recordMutations() {
  const s = "scheduling";
  const manifests = (await call(s, "Manifests", { take: 50 })).operations.manifests.items;
  const byExternal = Object.fromEntries(manifests.map((m) => [m.externalId, m]));
  const digest = byExternal["send-daily-digest"];
  const refresh = byExternal["refresh-exchange-rates"];
  const groups = (await call(s, "ManifestGroups", { take: 50 })).operations.manifestGroups.groups.items;
  const digestGroup = groups.find((g) => g.id === digest.manifestGroupId);
  const runs = (await call(s, "Executions", { take: 50, hideAdminTrains: true })).operations.executions.items;
  const completed = runs.filter((r) => r.trainState === "COMPLETED");
  const failed = runs.find((r) => r.trainState === "FAILED");
  const queued = (await call(s, "WorkQueue", { take: 50, status: "QUEUED" })).operations.workQueue.workQueues.items;
  const dispatched = (await call(s, "WorkQueue", { take: 50, status: "DISPATCHED" })).operations.workQueue.workQueues
    .items;
  const awaiting = await awaitingDeadLetters();
  const resolved = (await call(s, "DeadLetters", { take: 50, status: "RETRIED" })).operations.deadLetters.deadLetters
    .items;

  // Each write meets its rows as the snapshot shows them where it can: a row is changed once.
  await mutate(s, "CancelExecution", { id: completed[0].id });
  await mutate(s, "CancelExecutions", { ids: [completed[1].id, completed[2].id] });
  await mutate(s, "RequeueExecution", { id: failed.id, askAfresh: false });
  await mutate(s, "RequeueExecution", { id: completed[3].id, askAfresh: true });
  await mutate(s, "CancelWorkQueueEntry", { id: queued[0].id });
  await mutate(s, "CancelWorkQueueEntries", { ids: queued.slice(1).map((q) => q.id) });
  await mutate(s, "CancelWorkQueueEntry", { id: dispatched[0].id });
  await mutate(s, "QueueTrain", { input: { trainName: digest.name, inputJson: "{ not json", priority: 0 } });
  await mutate(s, "QueueTrain", { input: { trainName: "Trax.Samples.NoSuchTrain", inputJson: "{}", priority: 0 } });
  await mutate(s, "RunTrain", { input: { trainName: digest.name, inputJson: JSON.stringify({ audience: "staff" }) } });

  if (resolved[0]) await mutate(s, "RequeueDeadLetter", { id: resolved[0].id, askAfresh: false });
  if (resolved[0]) await mutate(s, "AcknowledgeDeadLetter", { id: resolved[0].id, note: "Handled by hand." });
  const dl = awaiting[0];
  if (dl) {
    await mutate(s, "RequeueDeadLetters", { ids: [dl.id], askAfresh: false });
    await mutate(s, "AcknowledgeDeadLetters", { ids: [dl.id], note: "Handled by hand." });
    await mutate(s, "RequeueDeadLetter", { id: dl.id, askAfresh: true });
  }
  const job = await mutate(s, "RequeueAllDeadLetters", { askAfresh: false });
  const jobId = job.data?.operations?.deadLetters?.requeueAllDeadLetters?.id;
  if (jobId) {
    await sleep(1500);
    await record(s, "RequeueAllJob", { id: jobId });
  }
  await mutate(s, "AcknowledgeAllDeadLetters", { note: "Handled by hand." });

  await mutate(s, "UpdateManifest", {
    id: digest.id,
    input: { isEnabled: true, maxRetries: digest.maxRetries, priority: 3, clearTimeout: true },
  });
  await mutate(s, "UpdateManifest", { id: digest.id, input: { scheduleType: "CRON", cronExpression: "not a cron" } });
  await mutate(s, "TriggerManifest", { externalId: digest.externalId, askAfresh: false });
  await mutate(s, "TriggerManifest", { externalId: "no-such-manifest", askAfresh: false });
  await mutate(s, "DisableManifest", { externalId: digest.externalId });
  await mutate(s, "TriggerManifest", { externalId: digest.externalId, askAfresh: true });
  await mutate(s, "EnableManifest", { externalId: digest.externalId });
  await mutate(s, "SetManifestsEnabled", { ids: [digest.id, refresh.id], enabled: false });
  await mutate(s, "SetManifestsEnabled", { ids: [digest.id, refresh.id], enabled: true });
  await mutate(s, "SetManifestsReplayDecisionsOnRetry", { ids: [digest.id], replay: true });
  await mutate(s, "SetManifestsReplayDecisionsOnRetry", { ids: [digest.id], replay: false });
  await mutate(s, "TriggerManifests", { ids: manifests.map((m) => m.id), askAfresh: false });
  await mutate(s, "TriggerManifests", { ids: [digest.id], askAfresh: true });

  await mutate(s, "UpdateManifestGroup", { id: digestGroup.id, input: { maxActiveJobs: 2, priority: 1, isEnabled: true } });
  await mutate(s, "TriggerGroup", { groupId: digestGroup.id });
  await mutate(s, "CancelGroup", { groupId: digestGroup.id });
  await mutate(s, "TriggerGroups", { ids: groups.map((g) => g.id) });
  await mutate(s, "CancelGroups", { ids: groups.map((g) => g.id) });
  await mutate(s, "SetManifestGroupsEnabled", { ids: [digestGroup.id], enabled: false });
  await mutate(s, "SetManifestGroupsEnabled", { ids: [digestGroup.id], enabled: true });
  await mutate(s, "SetAllManifestGroupsEnabled", { enabled: false });
  await mutate(s, "SetAllManifestGroupsEnabled", { enabled: true });

  await mutate(s, "UpdateScheduler", { input: { maxActiveJobs: 10, defaultMaxRetries: 3 } });
  await mutate(s, "UpdateScheduler", { input: { clearMaxActiveJobs: true } });
  const levels = (await call(s, "LogLevels")).operations.config.logLevels;
  await mutate(s, "SetLogLevels", { levels: [{ category: levels[0]?.category ?? "Default", level: "WARNING" }] });
  await mutate(s, "SetLogLevels", { levels: [{ category: "Not.Configured.Category", level: "DEBUG" }] });
  for (const name of SCHEDULER_HOSTS) {
    const effects = (await call(name, "Effects")).operations.effects;
    for (const e of effects.filter((x) => x.toggleable)) {
      await mutate(name, "SetEffectEnabled", { fullName: e.fullName, enabled: !e.enabled });
      await mutate(name, "SetEffectEnabled", { fullName: e.fullName, enabled: e.enabled });
    }
    for (const e of effects.filter((x) => x.isConfigurable)) {
      const values = e.fields.filter((f) => !f.sensitive).map((f) => ({ name: f.name, value: f.value }));
      await mutate(name, "ConfigureEffect", { fullName: e.fullName, values });
      if (e.fields[0]) await mutate(name, "ConfigureEffect", { fullName: e.fullName, values: [{ name: e.fields[0].name, value: "not-a-value-it-takes" }] });
    }
  }

  // Recovery: ask a finished run afresh, and the replay switch on its manifests.
  const recoveryRuns = (await call("recovery", "Executions", { take: 50, hideAdminTrains: true })).operations.executions
    .items;
  const recoveryDone = recoveryRuns.find((r) => r.trainState === "COMPLETED");
  if (recoveryDone) await mutate("recovery", "RequeueExecution", { id: recoveryDone.id, askAfresh: true });

  // Resume a run from its graph: a failed attempt from where it stopped and from a named step, and a run that
  // completed, which has nothing to resume.
  const recoveryFailed = (await call("recovery", "Executions", { take: 50, trainState: "FAILED", hideAdminTrains: true })).operations
    .executions.items;
  for (const run of recoveryFailed.slice(0, 2)) {
    await mutate("recovery", "ResumeExecution", { id: run.id });
    const graph = (await call("recovery", "RunGraph", { metadataId: run.id })).operations.runGraph;
    const from = graph?.allNodes.find((n) => n.canResume)?.id;
    if (from) await mutate("recovery", "ResumeExecution", { id: run.id, from });
  }
  if (recoveryDone) await mutate("recovery", "ResumeExecution", { id: recoveryDone.id });

  // The operator's one action on an instance: a user's order (refused, it is not the system's) and a partition.
  for (const machine of ["order", "source-partition"]) {
    const page = (await call("recovery", "MachineInstancesAttention", { machine, skip: 0, take: MACHINE_PAGE })).operations
      .machineInstances;
    const instance = page.items[0];
    if (instance)
      await mutate("recovery", "CancelMachineInstance", { machine, ownerKind: instance.ownerKind, id: instance.id });
  }

  // Bulk operations from a list's "all matching": one refused because the list moved under it, one that runs.
  const schedules = (await call(s, "Manifests", { take: 100, hideAdminTrains: true })).operations.manifests.items;
  const all = { manifests: { hideAdminTrains: true } };
  await mutate(s, "StartBulkOperation", { kind: "DISABLE", filter: all, expectedCount: schedules.length + 1, askAfresh: false });
  await mutate(s, "StartBulkOperation", { kind: "ENABLE", filter: all, expectedCount: schedules.length, askAfresh: false });

  const p = "persisted";
  const stored = (await call(p, "PersistedOperationDetail", { id: "greet_v1", historyTake: 1 })).persistedOperations
    .persistedOperation;
  await mutate(p, "UploadPersistedOperation", { input: { id: "greet_v1", document: stored.document } });
  await mutate(p, "UploadPersistedOperation", { input: { id: "greet_v1", document: "query { nope" } });
  await mutate(p, "RestorePersistedOperation", { input: { id: "greet_v0" } });
  await mutate(p, "DeactivatePersistedOperation", { input: { id: "greet_v0", reason: "Every client sends greet_v1 now." } });
  await mutate(p, "RestorePersistedOperation", { input: { id: "missing_v9" } });
}

// ── The website's copy ───────────────────────────────────────────────────────

// Which fields of each response hold an id, and of what kind.
const ID_FIELDS = {
  Executions: { id: "metadata", parentId: "metadata", manifestId: "manifest" },
  ExecutionChildren: { id: "metadata" },
  ExecutionDetail: { id: "metadata", parentId: "metadata", replayDecisionsOf: "metadata", manifestId: "manifest" },
  WorkQueue: { id: "workQueue", manifestId: "manifest", replayDecisionsOf: "metadata" },
  WorkQueueDetail: {
    id: "workQueue",
    manifestId: "manifest",
    metadataId: "metadata",
    deadLetterId: "deadLetter",
    replayDecisionsOf: "metadata",
    subjectHeldBy: "workQueue",
    subjectQueuedBehind: "workQueue",
  },
  DeadLetters: { id: "deadLetter", manifestId: "manifest" },
  DeadLetterDetail: { id: "deadLetter", manifestId: "manifest", retryMetadataId: "metadata" },
  Logs: { id: "log", metadataId: "metadata" },
  Manifests: { id: "manifest", manifestGroupId: "group" },
  ManifestDetail: { id: "manifest", manifestGroupId: "group", dependsOnManifestId: "manifest" },
  ManifestStats: { manifestId: "manifest" },
  ManifestGroups: { id: "group" },
  ManifestGroupDetail: { id: "group", fromId: "group", toId: "group" },
  GroupDependencyGraph: { id: "group", fromId: "group", toId: "group" },
  ManifestGroupStats: { groupId: "group" },
  Decisions: { id: "decision", metadataId: "metadata" },
  OnTrainStateChanged: { metadataId: "metadata" },
  OnJunctionEvent: { metadataId: "metadata" },
  RequeueDeadLetter: { workQueueId: "workQueue" },
  QueueTrain: { id: "workQueue" },
  RequeueExecution: { id: "workQueue" },
  RunTrain: { id: "metadata" },
  TriggerManifests: { id: "manifest" },
  TriggerGroups: { id: "group" },
  ResumeExecution: { id: "workQueue" },
  RunGraph: { metadataId: "metadata" },
  RunLinks: {
    id: "metadata",
    manifestId: "manifest",
    parentId: "metadata",
    replayDecisionsOf: "metadata",
    workQueueEntryId: "workQueue",
    deadLetterId: "deadLetter",
  },
  ManifestSchedule: { id: "manifest" },
  ManifestSchedules: { id: "manifest" },
  GroupGraphStatus: { groupId: "group" },
  MachineInstancesAttention: { executionId: "metadata", workQueueEntryId: "workQueue" },
  // An instance's own id is a UUID; the numeric id is its invoked runs'.
  MachineInstance: { id: "metadata", executionId: "metadata", workQueueEntryId: "workQueue", queuedInvokedRunEntryId: "workQueue" },
  MachineInstanceTransitions: { executionId: "metadata", workQueueEntryId: "workQueue" },
  // A bulk operation's id is a UUID; its failures' ids are rows of the kind it acts on (rowKindOf).
  BulkOperation: { manifestId: "manifest", manifestGroupId: "group", parentId: "metadata" },
  StartBulkOperation: { manifestId: "manifest", manifestGroupId: "group", parentId: "metadata" },
};
// The rows each bulk operation kind acts on.
const BULK_ROWS = {
  CANCEL: "metadata",
  REQUEUE: "deadLetter",
  ACKNOWLEDGE: "deadLetter",
  TRIGGER: "manifest",
  ENABLE: "manifest",
  DISABLE: "manifest",
};
// Which variables hold an id, and of what kind.
const ID_VARS = {
  Executions: { afterId: "metadata", parentId: "metadata", manifestId: "manifest", manifestGroupId: "group" },
  ExecutionChildren: { parentId: "metadata", afterId: "metadata" },
  ExecutionDetail: { id: "metadata" },
  JunctionRuns: { metadataId: "metadata" },
  Decisions: { metadataId: "metadata", afterId: "decision" },
  WorkQueue: { afterId: "workQueue", manifestId: "manifest" },
  WorkQueueDetail: { id: "workQueue" },
  DeadLetters: { afterId: "deadLetter", manifestId: "manifest" },
  DeadLetterDetail: { id: "deadLetter" },
  Logs: { afterId: "log", metadataId: "metadata" },
  Manifests: { afterId: "manifest", manifestGroupId: "group" },
  ManifestDetail: { id: "manifest" },
  ManifestStats: { manifestId: "manifest" },
  ManifestExclusions: { manifestId: "manifest" },
  ManifestGroups: { afterId: "group" },
  ManifestGroupDetail: { id: "group" },
  ManifestGroupStats: { groupIds: "group" },
  OnJunctionEvent: { metadataId: "metadata" },
  RequeueDeadLetter: { id: "deadLetter" },
  AcknowledgeDeadLetter: { id: "deadLetter" },
  RequeueDeadLetters: { ids: "deadLetter" },
  AcknowledgeDeadLetters: { ids: "deadLetter" },
  CancelWorkQueueEntries: { ids: "workQueue" },
  CancelWorkQueueEntry: { id: "workQueue" },
  CancelExecution: { id: "metadata" },
  CancelExecutions: { ids: "metadata" },
  RequeueExecution: { id: "metadata" },
  UpdateManifest: { id: "manifest" },
  TriggerManifests: { ids: "manifest" },
  SetManifestsEnabled: { ids: "manifest" },
  SetManifestsReplayDecisionsOnRetry: { ids: "manifest" },
  TriggerGroups: { ids: "group" },
  CancelGroups: { ids: "group" },
  TriggerGroup: { groupId: "group" },
  CancelGroup: { groupId: "group" },
  UpdateManifestGroup: { id: "group" },
  SetManifestGroupsEnabled: { ids: "group" },
  ResumeExecution: { id: "metadata" },
  RunGraph: { metadataId: "metadata" },
  RunLinks: { id: "metadata" },
  ManifestSchedule: { id: "manifest" },
  ManifestSchedules: { afterId: "manifest", manifestGroupId: "group" },
  GroupGraphStatus: { groupIds: "group" },
};
// The lists each page cuts, and the page sizes it asks for, by the variables that pick them.
const TAKES = {
  Executions: (v) =>
    v.startedAfter != null ? [500]
    : v.manifestId != null && v.trainState === "FAILED" && v.order == null ? [1, 10]
    : v.manifestId != null || v.manifestGroupId != null ? [10]
    : [25],
  ExecutionChildren: () => [25],
  WorkQueue: () => [25],
  DeadLetters: () => [25],
  Logs: (v) => (v.metadataId != null && v.order === "OLDEST" ? [25] : [50]),
  Manifests: () => [25],
  ManifestSchedules: () => [25],
  ManifestGroups: () => [25],
};

/** One sequence of ids per kind, shared by every host, ordered by time where the row has one. */
class IdSpace {
  constructor() {
    this.pending = new Map(); // kind -> host -> [{ id, time }]
    this.map = new Map(); // `${kind}:${host}:${id}` -> new id
    this.next = new Map();
  }
  add(kind, host, id, time) {
    if (!this.pending.has(kind)) this.pending.set(kind, new Map());
    const byHost = this.pending.get(kind);
    if (!byHost.has(host)) byHost.set(host, []);
    byHost.get(host).push({ id, time: time ? Date.parse(time) : null });
  }
  /** Interleaves each host's rows (kept in their own id order) by time, and numbers them from 1. */
  finish() {
    const hostOrder = Object.keys(HOSTS);
    for (const [kind, byHost] of this.pending) {
      const queues = [...byHost.entries()]
        .sort(([a], [b]) => hostOrder.indexOf(a) - hostOrder.indexOf(b))
        .map(([host, rows]) => ({ host, rows: [...new Map(rows.map((r) => [r.id, r])).values()].sort((a, b) => a.id - b.id) }));
      let n = 0;
      for (;;) {
        let pick = null;
        for (const q of queues) {
          if (!q.rows.length) continue;
          const head = q.rows[0];
          if (!pick) pick = q;
          else {
            const best = pick.rows[0];
            if (head.time != null && best.time != null ? head.time < best.time : false) pick = q;
          }
        }
        if (!pick) break;
        const row = pick.rows.shift();
        this.map.set(`${kind}:${pick.host}:${row.id}`, ++n);
      }
      this.next.set(kind, n + 1);
    }
  }
  get(kind, host, id) {
    // 0 is no row (a log written outside any run), not an id.
    if (id == null || id === 0) return id;
    const key = `${kind}:${host}:${id}`;
    if (!this.map.has(key)) {
      const n = this.next.get(kind) ?? 1;
      this.map.set(key, n);
      this.next.set(kind, n + 1);
    }
    return this.map.get(key);
  }
}

/** The id fields of one answer: a bulk operation's failures are rows of the kind it acts on. */
function idFieldsOf(op, data) {
  const fields = ID_FIELDS[op] ?? {};
  const kind = op === "BulkOperation" ? data?.operations?.bulkOperation?.kind : null;
  return kind ? { ...fields, id: BULK_ROWS[kind] } : fields;
}

function remapData(ids, host, op, value, fields = idFieldsOf(op, value)) {
  const visit = (v) => {
    if (Array.isArray(v)) return v.map(visit);
    if (v && typeof v === "object") {
      const outObj = {};
      for (const [k, x] of Object.entries(v))
        outObj[k] = fields[k] && typeof x === "number" ? ids.get(fields[k], host, x) : visit(x);
      return outObj;
    }
    return v;
  };
  return visit(value);
}

function remapVars(ids, host, op, vars, data) {
  const kinds = { ...(ID_VARS[op] ?? {}) };
  // A series is of a manifest or a group by its scope; a bulk operation's failures page by their rows' ids.
  if (op === "MetricSeries" && vars.scope !== "TRAIN") kinds.id = vars.scope === "MANIFEST" ? "manifest" : "group";
  const bulkKind = op === "BulkOperation" ? data?.operations?.bulkOperation?.kind : null;
  if (bulkKind) kinds.failuresAfter = BULK_ROWS[bulkKind];
  const outVars = {};
  for (const [k, v] of Object.entries(vars)) {
    const kind = kinds[k];
    outVars[k] = !kind ? v : Array.isArray(v) ? v.map((x) => ids.get(kind, host, x)) : typeof v === "number" ? ids.get(kind, host, v) : v;
  }
  return outVars;
}

// The same key the dashboard's fixture exchange derives (src/mock/variables-hash.ts): nulls dropped, keys sorted.
function normalize(value) {
  if (Array.isArray(value)) return value.map(normalize);
  if (value && typeof value === "object") {
    const o = {};
    for (const key of Object.keys(value).sort()) {
      if (value[key] === null || value[key] === undefined) continue;
      o[key] = normalize(value[key]);
    }
    return o;
  }
  return value;
}
const hashVariables = (vars) => JSON.stringify(normalize(vars ?? {}));

function setAt(path, page) {
  return path.reduceRight((acc, key) => ({ [key]: acc }), page);
}

/**
 * The Trax version the footer shows instead of a local build's 1.99.99: the latest release tag of this
 * repository, whose packages are all released under one version.
 */
function pinnedApiVersion() {
  try {
    const tag = execFileSync("git", ["describe", "--tags", "--abbrev=0", "--match", "v[0-9]*"], { cwd: repo, encoding: "utf8" });
    return tag.trim().replace(/^v/, "");
  } catch {
    return "1.99.99";
  }
}

/** Replaces what names this machine: its name, its processes, its file paths, and the demo keys. */
function scrubber(recording) {
  const version = pinnedApiVersion();
  const replacements = [];
  for (const [name, h] of Object.entries(recording.hosts)) {
    h.instanceIds.forEach((instance, i) => replacements.push([instance, `${HOSTS[name].alias}-${i + 1}`]));
  }
  const user = userInfo().username;
  const home = recording.home ?? homedir();
  const forbidden = [hostname().replace(/\.local$/, ""), homedir(), home, user, "claude-501"].filter((x) => x && x.length > 2);
  const scrubString = (s, host) => {
    let r = s;
    for (const [from, to] of replacements) r = r.split(from).join(to);
    const machine = recording.hosts[host]?.machine;
    if (machine) r = r.split(machine).join(HOSTS[host].alias);
    // A path under the home directory the hosts ran in (ASP.NET's key store, say).
    if (home.length > 2) r = r.split(`${home}/`).join("~/");
    // An absolute path up to the repository it is in: /…/Trax.Scheduler/src/x.cs -> /src/Trax.Scheduler/src/x.cs.
    // Each segment stops at the next "/", so a path splits into segments only one way (no backtracking blow-up).
    r = r.replace(/(?:\/[^\s"'():/]*)+?\/(Trax\.[A-Za-z]+(?:\.[A-Za-z]+)*)\/(?=src\/|samples\/|tests\/)/g, "/src/$1/");
    r = r.replace(/[a-z]+(?:-[a-z]+)*-key-do-not-use-in-production/g, "demo-key-do-not-use-in-production");
    if (r === "1.99.99") r = version;
    return r;
  };
  const scrub = (value, host) => {
    if (typeof value === "string") return scrubString(value, host);
    if (Array.isArray(value)) return value.map((v) => scrub(v, host));
    if (value && typeof value === "object")
      return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, scrub(v, host)]));
    return value;
  };
  return { scrub, forbidden };
}

const WORSE = { Healthy: 0, Degraded: 1, Unhealthy: 2 };

/** Overview as one database would answer it: counts summed, the worse health, the busier series. */
function mergeOverview(parts) {
  const [first, ...rest] = parts;
  const out = structuredClone(first);
  const health = out.operations.health;
  const dash = out.operations.metrics.dashboard;
  let weighted = dash.kpis.successRate * dash.kpis.executionsToday;
  let total = dash.kpis.executionsToday;
  const sumBy = (rows, keyOf, add) => {
    const map = new Map(rows.map((r) => [keyOf(r), structuredClone(r)]));
    return (other) => {
      for (const r of other) {
        const k = keyOf(r);
        if (map.has(k)) add(map.get(k), r);
        else map.set(k, structuredClone(r));
      }
      return [...map.values()];
    };
  };
  for (const part of rest) {
    const h = part.operations.health;
    for (const k of ["queueDepth", "inProgress", "failedLastHour", "deadLetters"]) health[k] += h[k];
    if ((WORSE[h.status] ?? 0) > (WORSE[health.status] ?? 0)) {
      health.status = h.status;
      health.description = h.description;
    }
    const d = part.operations.metrics.dashboard;
    for (const k of ["executionsToday", "currentlyRunning", "unresolvedDeadLetters"]) dash.kpis[k] += d.kpis[k];
    weighted += d.kpis.successRate * d.kpis.executionsToday;
    total += d.kpis.executionsToday;
    dash.executionsOverTime = sumBy(dash.executionsOverTime, (r) => r.timestamp, (a, b) => {
      a.completed += b.completed;
      a.failed += b.failed;
      a.cancelled += b.cancelled;
    })(d.executionsOverTime).sort((a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp));
    const top = Math.max(dash.topFailures.length, d.topFailures.length);
    dash.topFailures = sumBy(dash.topFailures, (r) => r.trainName, (a, b) => (a.count += b.count))(d.topFailures)
      .sort((a, b) => b.count - a.count)
      .slice(0, top);
    const topDur = Math.max(dash.topAverageDurations.length, d.topAverageDurations.length);
    dash.topAverageDurations = sumBy(dash.topAverageDurations, (r) => r.trainName, (a, b) => {
      a.averageMilliseconds = Math.max(a.averageMilliseconds, b.averageMilliseconds);
    })(d.topAverageDurations)
      .sort((a, b) => b.averageMilliseconds - a.averageMilliseconds)
      .slice(0, topDur);
    dash.throughputSeries = sumBy(dash.throughputSeries, (r) => r.trainName, (a, b) => {
      a.buckets = sumBy(a.buckets, (x) => x.timestamp, (x, y) => (x.count += y.count))(b.buckets).sort(
        (x, y) => Date.parse(x.timestamp) - Date.parse(y.timestamp),
      );
    })(d.throughputSeries);
  }
  // The API reports the rate to one decimal place.
  dash.kpis.successRate = total > 0 ? Math.round((weighted / total) * 10) / 10 : dash.kpis.successRate;
  return out;
}

/** A train's series on both hosts, as one database would count it: the buckets of the same start summed. */
function mergeSeries(parts) {
  const out = structuredClone(parts[0]);
  const series = out.operations.metrics.series;
  const byStart = new Map(series.buckets.map((b) => [b.start, b]));
  for (const part of parts.slice(1)) {
    const other = part.operations.metrics.series;
    if (Date.parse(other.from) < Date.parse(series.from)) series.from = other.from;
    if (Date.parse(other.to) > Date.parse(series.to)) series.to = other.to;
    for (const b of other.buckets) {
      const mine = byStart.get(b.start);
      if (!mine) byStart.set(b.start, structuredClone(b));
      else {
        for (const k of ["completed", "failed", "cancelled"]) mine[k] += b[k];
        // A percentile of two hosts' runs is not either host's; the slower one is the honest bound.
        for (const k of ["p50DurationMs", "p95DurationMs"])
          if (b[k] != null && (mine[k] == null || b[k] > mine[k])) mine[k] = b[k];
      }
    }
  }
  series.buckets = [...byStart.values()].sort((a, b) => Date.parse(a.start) - Date.parse(b.start));
  return out;
}

/** What needs attention on both hosts: their lists together, the queue summed, the oldest entry the older. */
function mergeAttention(parts) {
  const out = structuredClone(parts[0]);
  const a = out.operations.attention;
  for (const part of parts.slice(1)) {
    const b = part.operations.attention;
    for (const k of ["deadLetters", "failingTrains", "stuckInstances", "offlineHosts"]) a[k] = [...a[k], ...structuredClone(b[k])];
    a.queue.queued += b.queue.queued;
    if (b.queue.oldestQueuedAt && (!a.queue.oldestQueuedAt || Date.parse(b.queue.oldestQueuedAt) < Date.parse(a.queue.oldestQueuedAt))) {
      a.queue.oldestQueuedAt = b.queue.oldestQueuedAt;
      a.queue.oldestAgeSeconds = b.queue.oldestAgeSeconds;
    }
    if (Date.parse(b.asOf) > Date.parse(a.asOf)) a.asOf = b.asOf;
  }
  a.deadLetters.sort((x, y) => y.count - x.count);
  a.failingTrains.sort((x, y) => y.lastHourFailureRate - x.lastHourFailureRate);
  return out;
}

const unionBy = (key) => (lists) => {
  const map = new Map();
  for (const list of lists) for (const item of list) if (!map.has(key(item))) map.set(key(item), item);
  return [...map.values()];
};

function mergeTrainStats(parts) {
  const out = structuredClone(parts[0]);
  const s = out.operations.trainStats;
  for (const part of parts.slice(1)) {
    const t = part.operations.trainStats;
    const runs = s.completed + t.completed;
    s.averageMilliseconds =
      runs > 0 ? ((s.averageMilliseconds ?? 0) * s.completed + (t.averageMilliseconds ?? 0) * t.completed) / runs : s.averageMilliseconds;
    for (const k of ["total", "completed", "failed", "inProgress", "pending", "cancelled"]) s[k] += t[k];
    for (const k of ["lastRun", "lastSuccessfulRun"])
      if (t[k] && (!s[k] || Date.parse(t[k]) > Date.parse(s[k]))) s[k] = t[k];
  }
  return out;
}

// Writes whose answer depends on every row of a kind, so no later state can be told apart.
const STATE_WIDE = new Set(["RequeueAllDeadLetters", "AcknowledgeAllDeadLetters", "SetAllManifestGroupsEnabled"]);

/** The rows a write names: its id variables by kind, and a manifest, effect or operation it names by key. */
function targetsOf(op, vars) {
  const kinds = ID_VARS[op] ?? {};
  const out = [];
  for (const [k, v] of Object.entries(vars)) {
    if (kinds[k]) for (const id of [v].flat()) out.push(`${kinds[k]}:${id}`);
    if (k === "externalId" || k === "fullName") out.push(`${k}:${v}`);
  }
  if (vars.input?.id) out.push(`persisted:${vars.input.tenantKey ?? ""}:${vars.input.id}`);
  // A write with no row (queue a train, change a setting) is judged by its input alone.
  if (out.length === 0) out.push(`${op}:${JSON.stringify(vars)}`);
  return out;
}

function refusedWrite(value) {
  if (Array.isArray(value)) return value.some(refusedWrite);
  if (value && typeof value === "object") {
    if (value.success === false) return true;
    if (Array.isArray(value.errors) && value.errors.length > 0) return true;
    return Object.values(value).some(refusedWrite);
  }
  return false;
}

/** Builds the website's copy from the raw recording. */
function compile(recording) {
  const ids = new IdSpace();
  const timeOf = { Executions: "startTime", WorkQueue: "createdAt", DeadLetters: "deadLetteredAt", ManifestGroups: "createdAt" };
  const kindOf = { Executions: "metadata", Manifests: "manifest", ManifestGroups: "group", WorkQueue: "workQueue", DeadLetters: "deadLetter", Logs: "log" };
  for (const w of recording.walks.filter((x) => x.scope === "base"))
    for (const item of w.items) ids.add(kindOf[w.op], w.host, item.id, item[timeOf[w.op]]);
  for (const q of recording.queries.filter((x) => x.op === "Decisions"))
    for (const d of q.data.operations.decisions.items) ids.add("decision", q.host, d.id, d.decidedAt);
  ids.finish();

  const { scrub, forbidden } = scrubber(recording);
  const queries = {};
  const put = (op, vars, data) => {
    queries[op] ??= {};
    queries[op][hashVariables(vars)] = data;
  };

  // Lists: every walk is cut into the page sizes the dashboard asks for; walks of one filter on both hosts are
  // combined first.
  const groupsOfWalks = new Map();
  for (const w of recording.walks) {
    if (w.scope === "base") continue;
    let vars = remapVars(ids, w.host, w.op, w.vars);
    if (w.scope === "host") vars = { ...vars, hostName: HOSTS[w.host].alias };
    const key = `${w.op}\u0000${hashVariables(vars)}`;
    if (!groupsOfWalks.has(key)) groupsOfWalks.set(key, { op: w.op, vars, parts: [] });
    groupsOfWalks.get(key).parts.push(w);
  }
  for (const { op, vars, parts } of groupsOfWalks.values()) {
    const items = parts.flatMap((w) => scrub(remapData(ids, w.host, op, w.items), w.host));
    const descending = vars.order !== "OLDEST";
    items.sort((a, b) => (descending ? b.id - a.id : a.id - b.id));
    const envelope = {};
    for (const w of parts)
      for (const [k, v] of Object.entries(w.envelope)) {
        if (typeof v === "boolean") envelope[k] = Boolean(envelope[k]) || v;
        else if (typeof v === "number") envelope[k] = (envelope[k] ?? 0) + v;
        else envelope[k] = v;
      }
    // Every row was read, so the count is exact.
    if ("totalCount" in envelope) envelope.totalCount = items.length;
    if ("isEstimatedCount" in envelope) envelope.isEstimatedCount = false;
    // The dead letters view groups the newest of the same list, read as one window.
    if (op === "DeadLetters") {
      const window = items.slice(0, DEAD_LETTER_GROUP_WINDOW);
      const cursor = window.length > 0 ? window[window.length - 1].id : null;
      put("DeadLetterGroups", { ...vars, take: DEAD_LETTER_GROUP_WINDOW }, setAt(PAGE_PATHS.DeadLetters, { ...envelope, items: window, nextCursor: cursor }));
    }
    for (const take of TAKES[op](vars)) {
      let afterId = null;
      for (let at = 0; ; at += take) {
        const page = items.slice(at, at + take);
        const cursor = page.length > 0 ? page[page.length - 1].id : null;
        const paged = { ...envelope, items: page, nextCursor: cursor };
        if ("take" in envelope) paged.take = take;
        put(op, { ...vars, take, afterId }, setAt(PAGE_PATHS[op], paged));
        if (page.length === 0) break;
        afterId = cursor;
      }
    }
  }

  // Everything else, by host; the singletons that a database holds are combined.
  const byOp = new Map();
  for (const q of recording.queries) {
    if (!byOp.has(q.op)) byOp.set(q.op, []);
    byOp.get(q.op).push(q);
  }
  const combined = {
    Overview: (parts) => mergeOverview(parts.map((p) => p.data)),
    Trains: (parts) => ({
      operations: { trains: unionBy((t) => t.fullName)(parts.map((p) => p.data.operations.trains)) },
    }),
    Effects: (parts) => ({
      operations: { effects: unionBy((e) => e.fullName)(parts.map((p) => p.data.operations.effects)) },
    }),
    Hosts: (parts) => ({ operations: { hosts: parts.flatMap((p) => p.data.operations.hosts) } }),
    AdminTrainNames: (parts) => ({
      operations: { adminTrainNames: [...new Set(parts.flatMap((p) => p.data.operations.adminTrainNames))] },
    }),
    GroupDependencyGraph: (parts) => ({
      operations: {
        manifestGroups: {
          dependencyGraph: {
            nodes: parts.flatMap((p) => p.data.operations.manifestGroups.dependencyGraph.nodes),
            edges: parts.flatMap((p) => p.data.operations.manifestGroups.dependencyGraph.edges),
          },
        },
      },
    }),
    TrainStats: (parts) => mergeTrainStats(parts.map((p) => p.data)),
    TrainsAskingDecisions: (parts) => ({
      operations: { trains: unionBy((t) => t.fullName)(parts.map((p) => p.data.operations.trains)) },
    }),
    TrainChains: (parts) => ({
      operations: { trains: unionBy((t) => t.fullName)(parts.map((p) => p.data.operations.trains)) },
    }),
    Machines: (parts) => ({
      operations: { machines: unionBy((m) => m.name)(parts.map((p) => p.data.operations.machines)) },
    }),
    MachineInstanceStuckCounts: (parts) => ({
      operations: { machineInstanceCounts: parts.flatMap((p) => p.data.operations.machineInstanceCounts) },
    }),
    BulkOperations: (parts) => ({
      operations: {
        bulkOperations: parts
          .flatMap((p) => p.data.operations.bulkOperations)
          .sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt)),
      },
    }),
    MetricSeries: (parts) => mergeSeries(parts.map((p) => p.data)),
    Attention: (parts) => mergeAttention(parts.map((p) => p.data)),
  };
  const processOnly = new Set(["SchedulerConfig", "EnvironmentName", "ServerVersion", "LogLevels", "ServerProcess"]);
  for (const [op, list] of byOp) {
    // The scheduler config read before the freeze is the one the page shows.
    const entries = op === "SchedulerConfig" ? list.filter((q) => q.host === PRIMARY).slice(0, 1) : list;
    const keyed = new Map();
    for (const q of entries) {
      if (processOnly.has(op) && q.host !== PRIMARY) continue;
      const vars = remapVars(ids, q.host, op, q.vars, q.data);
      const data = scrub(remapData(ids, q.host, op, q.data), q.host);
      const key = hashVariables(vars);
      if (!keyed.has(key)) keyed.set(key, { vars, parts: [] });
      keyed.get(key).parts.push({ host: q.host, data });
    }
    for (const { vars, parts } of keyed.values()) {
      const data = parts.length > 1 && combined[op] ? combined[op](parts) : parts[0].data;
      put(op, vars, data);
    }
  }
  // A page of groups asks for all its groups' stats at once.
  const stats = new Map();
  for (const data of Object.values(queries.ManifestGroupStats ?? {}))
    for (const s of data.operations.manifestGroups.stats) stats.set(s.groupId, s);
  for (const data of Object.values(queries.ManifestGroups ?? {})) {
    const groupIds = data.operations.manifestGroups.groups.items.map((g) => g.id);
    if (groupIds.length)
      put("ManifestGroupStats", { groupIds }, { operations: { manifestGroups: { stats: groupIds.map((g) => stats.get(g)).filter(Boolean) } } });
  }
  // A dependency graph tones its nodes by one read of all their groups: the whole graph spans both hosts.
  const statuses = new Map();
  for (const data of Object.values(queries.GroupGraphStatus ?? {}))
    for (const s of data.operations.manifestGroups.stats) statuses.set(s.groupId, s);
  const graphs = [
    ...Object.values(queries.GroupDependencyGraph ?? {}).map((d) => d.operations.manifestGroups.dependencyGraph),
    ...Object.values(queries.ManifestGroupDetail ?? {}).map((d) => d.operations.manifestGroups.graph),
  ];
  for (const graph of graphs.filter(Boolean)) {
    const groupIds = graph.nodes.slice(0, GROUP_STATUS_IDS).map((n) => n.id);
    if (groupIds.length)
      put("GroupGraphStatus", { groupIds }, { operations: { manifestGroups: { stats: groupIds.map((g) => statuses.get(g)).filter(Boolean) } } });
  }
  // What needs attention shows the schedulers as they ran before the freeze, as the settings page does.
  const running = recording.attentionBeforeFreeze?.[PRIMARY]?.operations?.attention?.scheduler;
  if (running)
    for (const data of Object.values(queries.Attention ?? {})) data.operations.attention.scheduler = structuredClone(running);

  // Writes, keyed like the reads. A write is "current" when the host answered it in the state the
  // snapshot shows: sent after the snapshot, to rows no earlier write had changed. Only a current
  // write's refusal, or its message naming a row, is the demo's to repeat for the same variables.
  const mutations = {};
  const touched = new Set();
  for (const m of recording.mutations) {
    const vars = remapVars(ids, m.host, m.op, m.vars);
    const response = scrub(remapData(ids, m.host, m.op, m.response), m.host);
    const targets = targetsOf(m.op, vars);
    const current =
      m.phase === "mutations" && !STATE_WIDE.has(m.op) && targets.length > 0 && targets.every((t) => !touched.has(t));
    if (!refusedWrite(response)) for (const t of targets) touched.add(t);
    (mutations[m.op] ??= []).push({ variables: vars, current, ...response });
  }

  // Subscription frames, as times from the first frame of their stream.
  const t0 = Math.min(...recording.frames.map((f) => f.t));
  const frames = { OnTrainStateChanged: [], OnDataChanged: [], OnJunctionEvent: {} };
  for (const f of [...recording.frames].sort((a, b) => a.t - b.t)) {
    const data = scrub(remapData(ids, f.host, f.op, f.data), f.host);
    const frame = { t: f.t - t0, data };
    if (f.op === "OnJunctionEvent") {
      const metadataId = ids.get("metadata", f.host, f.vars.metadataId);
      (frames.OnJunctionEvent[metadataId] ??= []).push(frame);
    } else frames[f.op].push(frame);
  }

  // Rows that repeat across pages are stored once.
  const objects = [];
  const index = new Map();
  const intern = (value) => {
    if (Array.isArray(value)) return value.map(intern);
    if (value && typeof value === "object") {
      const o = Object.fromEntries(Object.entries(value).map(([k, v]) => [k, k === "items" && Array.isArray(v) ? { $refs: v.map(ref) } : intern(v)]));
      return o;
    }
    return value;
  };
  const ref = (item) => {
    const json = JSON.stringify(item);
    if (!index.has(json)) {
      index.set(json, objects.length);
      objects.push(item);
    }
    return index.get(json);
  };
  const copy = {
    recordedAt: recording.recordedAt,
    note: "Recorded by Trax.Samples scripts/recordings/dashboard.mjs from the Scheduling, Recovery and PersistedOperations samples. Do not edit by hand.",
    objects: null,
    queries: intern(queries),
    mutations,
    subscriptions: frames,
  };
  copy.objects = objects;

  const text = JSON.stringify(copy);
  for (const word of forbidden)
    if (text.includes(word)) throw new Error(`the website's copy still contains "${word}"; extend the scrubber`);
  return text;
}

function writeWebsiteCopy(dir, recording) {
  const text = compile(recording);
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "dashboard-recordings.json");
  writeFileSync(file, text + "\n");
  console.log(`wrote ${file}: ${(text.length / 1024).toFixed(0)} KB`);
}

// ── Main ─────────────────────────────────────────────────────────────────────

async function main() {
  DOCS = readDocuments();
  mkdirSync(out, { recursive: true });
  const file = join(out, "recording.json");

  if (args.includes("--copy-only")) {
    if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
    if (!existsSync(file)) throw new Error(`no recording at ${file}; record one first`);
    return writeWebsiteCopy(copyTo, JSON.parse(readFileSync(file, "utf8")));
  }

  log("building the three hosts");
  for (const name of Object.keys(HOSTS))
    execFileSync("dotnet", ["build", "--nologo", "-v", "q", join(repo, HOSTS[name].project)], { stdio: "inherit" });
  for (const name of Object.keys(HOSTS)) await ensurePortFree(HOSTS[name].port);
  for (const name of Object.keys(HOSTS)) startHost(name);
  await Promise.all([
    waitReady("scheduling", "Manifests", { take: 1 }),
    waitReady("recovery", "Manifests", { take: 1 }),
    waitReady("persisted", "PersistedOperations", { skip: 0, take: 1 }),
  ]);
  log("hosts up; driving them");

  const ws = await driveAll();
  log("freezing both schedulers");
  await freeze();
  for (const client of Object.values(ws)) await client.dispose();
  // The snapshot is of the next whole minute: the train page reads its failed runs from a minute (the one it
  // loaded in) back, and the demo moves every time by whole minutes, so the recorded range is the one it asks.
  const anchor = Math.ceil(Date.now() / 60_000) * 60_000;
  await sleep(anchor - Date.now());
  raw.recordedAt = new Date(anchor).toISOString();
  log("reading the snapshot");
  await snapshot();
  log("recording every write");
  await recordMutations();

  writeFileSync(file, JSON.stringify(raw));
  log(`recorded ${file}: ${raw.queries.length} reads, ${raw.walks.length} lists, ${raw.mutations.length} writes, ${raw.frames.length} frames`);
  stopHosts();
  if (copyTo) writeWebsiteCopy(copyTo, raw);
}

main()
  .then(() => process.exit(0))
  .catch((error) => {
    console.error(error);
    process.exit(1);
  });
