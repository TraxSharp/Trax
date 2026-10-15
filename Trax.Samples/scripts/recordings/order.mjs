// Records the Recovery sample's food order (the `order` machine) for traxsharp.net, which plays it back as one demo:
// the laptop that places the order, OrderMachine.cs with the acting line lit, the phone that follows it, and the run
// of DeliverOrderTrain its Ordered state invokes. Start the Recovery host on a fresh database with ad-hoc documents
// allowed (the recorder writes its documents by hand, as recovery.mjs does), the cart's wait shortened, and the
// machines swept every second (so the cart's timer fires when it is due rather than at the next minute's sweep), then
// run
//
//   Recovery__RequirePersistedOperations=false Recovery__CartWait=00:00:15 Recovery__MachineSweepInterval=00:00:01 \
//     dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
//   node order.mjs [--copy-to <dir>]
//
// One person, their own order, on two screens: the `laptop` sends every request, and both the laptop and the `phone`
// follow the order through `machineChanged` (the same user, two sockets); neither polls. Each scenario starts a fresh
// order with the machine's typed `start`, adds a Margherita, and presses Place order twice in a hurry: the second
// press is the same request (one request id), so the server replays its answer. Everything is written down with the
// time it arrived: each request and the server's whole answer, every push each screen received, and every step of
// the invoked run (`onJunctionEvent` with the operator key, the run found through `operations { executions }`).
//
// Four scenarios: order (the pizza arrives), tablet-times-out, restaurant-closed and left-in-cart. The trouble is
// chosen before the order is placed with the host's demo control, `dispatch { orderTrouble }`, which arms it for the
// next order (Faults/FaultInjector.cs). The tablet's retry happens inside SendToRestaurant, one junction of one run,
// so its attempts are read from the restaurant's own record of the calls its tablet received
// (`discover { orders { tabletCalls } }`). left-in-cart waits for the server's timer, which the host was started with
// at 15 seconds instead of 30 minutes; the idle stretch is compressed in `t`, and the first event after it carries
// `gap`, the milliseconds removed.
//
// out/order/<key>.json holds each scenario as recorded, and out/order-recordings.json the website's copy, the shape
// Trax.Website reads (src/data/order-recordings.json): OrderMachine.cs verbatim, the lines to light, the train's steps,
// the cart as the server priced it, the cart timer's duration from the machine's real IR as shipped (exported with the
// trax CLI into out/order/order.ir.json), and the four scenarios. --copy-to <dir> also writes it there; --copy-only rewrites it from out/ without recording.

import { createClient } from "graphql-ws";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const HOST = process.env.RECOVERY_HOST ?? "localhost:5260";
const KEY = "recovery-operator-key-do-not-use-in-production";
const OUT = join(here, "out", "order");
const MACHINE_FILE = "Trax.Samples.Recovery/Machines/OrderMachine.cs";
const MACHINE_SOURCE = resolve(here, "../../samples/Recovery", MACHINE_FILE);
const TRAIN_FILE = "Trax.Samples.Recovery/Trains/Order/DeliverOrderTrain.cs";
const TRAIN_SOURCE = resolve(here, "../../samples/Recovery", TRAIN_FILE);
const REPO = resolve(here, "../../..");
const PROJECT = resolve(here, "../../samples/Recovery/Trax.Samples.Recovery");
const STEPS = ["SendToRestaurant", "ChargeCard", "Cook", "PickUp", "Deliver"];
const POLL_MS = 100;
// A stretch with nothing happening longer than this is compressed to COMPRESSED_GAP in `t`.
const IDLE_MS = 4000;
const COMPRESSED_GAP = 1500;
// How far apart the two presses of Place order are: a double click.
const DOUBLE_CLICK_MS = 120;

const SCENARIOS = [
  {
    key: "order",
    title: "Order a pizza",
    caption: "You order a Margherita and follow it on your phone until it is at the door.",
    trouble: "NONE",
    end: "Delivered",
  },
  {
    key: "tablet-times-out",
    title: "The tablet doesn't answer",
    caption: "The restaurant's tablet doesn't answer in time, so the server tries again and the pizza still arrives.",
    trouble: "TABLET_TIMES_OUT",
    end: "Delivered",
  },
  {
    key: "restaurant-closed",
    title: "The restaurant is closed",
    caption: "The restaurant is closed, so the order is cancelled before your card is charged.",
    trouble: "RESTAURANT_CLOSED",
    end: "Cancelled",
  },
  {
    key: "left-in-cart",
    title: "Left in the cart",
    caption: "You never press Place order, so the server clears the cart by itself with nobody watching.",
    trouble: null,
    end: "Cleared",
  },
];

const ANSWER = "id revision state dueAt context { items total } problem { code message }";
const STEP = "position kind name state startedAt endedAt";

const ops = {
  start: `mutation ($requestId: String!) { machines { order { start(requestId: $requestId) { ${ANSWER} } } } }`,
  addItem: `mutation ($id: UUID!, $input: NewItemInput!, $requestId: String!) {
  machines { order { addItem(id: $id, input: $input, requestId: $requestId) { ${ANSWER} } } } }`,
  placeOrder: `mutation ($id: UUID!, $requestId: String!) { machines { order { placeOrder(id: $id, requestId: $requestId) { ${ANSWER} } } } }`,
  machineChanged: `subscription ($id: UUID!) { machineChanged(machine: "order", id: $id) {
  revision state gone problem { code message } } }`,
  orderTrouble: `mutation ($trouble: OrderTrouble!) { dispatch { orderTrouble(input: { trouble: $trouble }) { output { trouble } } } }`,
  // The run Ordered queued. A user's order lists only the run its state waits on, and a run that fails at once can be
  // over before the order is read, so the run is found as the first DeliverOrderTrain run since the order was placed.
  runs: `query ($after: DateTime!) { operations { executions(trainName: "Trax.Samples.Recovery.Trains.Order.IDeliverOrderTrain",
  startedAfter: $after, order: OLDEST, take: 1) { items { id } } } }`,
  onJunctionEvent: `subscription ($metadataId: Long!) { onJunctionEvent(metadataId: $metadataId) { junction { ${STEP} } } }`,
  junctionRuns: `query ($metadataId: Long!) { operations { junctionRuns(metadataId: $metadataId) { ${STEP} } } }`,
  execution: `query ($id: Long!) { operations { executionDetail(id: $id) { trainState failureJunction failureReason } } }`,
  tabletCalls: `{ discover { orders { tabletCalls { restaurant calls { orderId attempt startedAt endedAt problem } } } } }`,
};

const ENDED = new Set(["COMPLETED", "FAILED", "CANCELLED"]);
const RANK = { IN_PROGRESS: 0, COMPLETED: 1, FAILED: 1, CANCELLED: 1 };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function post(query, variables) {
  const started = performance.now();
  const response = await fetch(`http://${HOST}/trax/graphql`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": KEY },
    body: JSON.stringify({ query, variables }),
  });
  const body = await response.json();
  if (body.errors?.length) throw new Error(`${body.errors[0].message} (${query.slice(0, 60)}...)`);
  return { data: body.data, ms: Math.round(performance.now() - started) };
}

const socket = () =>
  createClient({ url: `ws://${HOST}/trax/graphql`, connectionParams: { apiKey: KEY }, lazy: true });

async function record(scenario, sockets) {
  const t0 = Date.now();
  const now = () => Date.now() - t0;
  const at = (iso) => Date.parse(iso) - t0;
  const events = [];
  const answered = new Map(); // requestId -> revision of its first answer
  let counter = 0;
  const requestId = (what) => `${scenario.key}-${what}-${++counter}-${t0}`;

  if (scenario.trouble) await post(ops.orderTrouble, { trouble: scenario.trouble });

  /** One typed operation from the laptop: its request, and the server's whole answer. */
  async function send(operation, label, variables) {
    events.push({ t: now(), kind: "request", operation, requestId: variables.requestId, label });
    const { data, ms } = await post(ops[operation], variables);
    const answer = data.machines.order[operation];
    const event = {
      t: now(),
      kind: "answer",
      operation,
      requestId: variables.requestId,
      ms,
      revision: answer.problem ? null : answer.revision,
      state: answer.problem ? null : answer.state,
      problem: answer.problem ?? null,
    };
    if (answered.has(variables.requestId)) event.replayed = !event.problem && answered.get(variables.requestId) === event.revision;
    else answered.set(variables.requestId, event.revision);
    events.push(event);
    return answer;
  }

  // ── start: a fresh order, its id minted by the server ──
  const started = await send("start", "Start an order", { requestId: requestId("start") });
  if (started.problem) throw new Error(`${scenario.key}: start was refused: ${JSON.stringify(started.problem)}`);
  const id = started.id;

  // ── both screens follow it ──
  const latest = { laptop: 0, phone: 0 };
  const state = { laptop: started.state, phone: started.state };
  const unsubscribe = ["laptop", "phone"].map((screen) =>
    sockets[screen].subscribe(
      { query: ops.machineChanged, variables: { id } },
      {
        next: ({ data, errors }) => {
          if (errors?.length) throw new Error(`${screen} machineChanged: ${errors[0].message}`);
          const change = data?.machineChanged;
          if (!change || change.problem || change.gone || change.revision == null) return;
          if (change.revision <= latest[screen]) return;
          latest[screen] = change.revision;
          state[screen] = change.state;
          events.push({ t: now(), kind: "push", screen, revision: change.revision, state: change.state });
        },
        error: (e) => console.error(`  ${scenario.key} ${screen} machineChanged:`, e),
        complete: () => {},
      },
    ),
  );
  await waitFor(() => latest.laptop >= 1 && latest.phone >= 1, 5000, "both screens to receive the order");

  // ── the cart ──
  await sleep(600);
  const added = await send("addItem", "Add 1 × Margherita", { id, input: { item: "Margherita" }, requestId: requestId("add") });
  if (added.problem) throw new Error(`${scenario.key}: addItem was refused: ${JSON.stringify(added.problem)}`);
  // The cart as the server priced it, and the restaurant it is ordered from.
  const order = {
    item: added.context.items[0],
    priceCents: Math.round(added.context.total * 100),
    restaurant: (await post(ops.tabletCalls)).data.discover.orders.tabletCalls.restaurant,
  };
  await waitFor(() => latest.phone >= added.revision, 5000, "the phone to see the cart");

  if (scenario.end === "Cleared") {
    // Nobody places the order: the server's timer clears the cart.
    await waitFor(() => state.laptop === "Cleared" && state.phone === "Cleared", 120_000, "the cart's timer to fire");
  } else {
    // ── Place order, pressed twice in a hurry: one request, sent twice ──
    await sleep(800);
    const placedAt = new Date().toISOString();
    const press = requestId("place");
    const [first, second] = await Promise.all([
      send("placeOrder", "Place order", { id, requestId: press }),
      sleep(DOUBLE_CLICK_MS).then(() => send("placeOrder", "Place order (pressed again)", { id, requestId: press })),
    ]);
    if (first.problem) throw new Error(`${scenario.key}: placeOrder was refused: ${JSON.stringify(first.problem)}`);
    if (second.revision !== first.revision) throw new Error(`${scenario.key}: the second press was not a replay: ${JSON.stringify(second)}`);

    // ── the run Ordered waits on, and its steps ──
    const runId = await deliveryRun(placedAt);
    const steps = new Map(); // position -> step
    const merge = (step) => {
      const known = steps.get(step.position);
      if (!known || RANK[step.state] > RANK[known.state]) steps.set(step.position, step);
    };
    const stopJunctions = sockets.junctions.subscribe(
      { query: ops.onJunctionEvent, variables: { metadataId: runId } },
      {
        next: ({ data }) => data?.onJunctionEvent && merge(data.onJunctionEvent.junction),
        error: (e) => console.error(`  ${scenario.key} onJunctionEvent:`, e),
        complete: () => {},
      },
    );
    await sleep(100);
    for (const step of (await post(ops.junctionRuns, { metadataId: runId })).data.operations.junctionRuns) merge(step);

    // The run ends, and its outcome moves the order on, on both screens.
    let run = null;
    await waitFor(async () => {
      run = (await post(ops.execution, { id: runId })).data.operations.executionDetail;
      return run && ENDED.has(run.trainState);
    }, 60_000, "the delivery run to end");
    await waitFor(() => state.laptop === scenario.end && state.phone === scenario.end, 30_000, `the order to be ${scenario.end}`);
    for (const step of (await post(ops.junctionRuns, { metadataId: runId })).data.operations.junctionRuns) merge(step);
    stopJunctions();

    const calls = (await post(ops.tabletCalls)).data.discover.orders.tabletCalls.calls.filter((c) => at(c.startedAt) >= 0);
    events.push(...junctionEvents(at, [...steps.values()], calls, run));
  }

  await sleep(800);
  unsubscribe.forEach((stop) => stop());
  if (latest.phone !== latest.laptop) throw new Error(`${scenario.key}: the phone is at ${latest.phone}, the laptop at ${latest.laptop}`);

  events.sort((a, b) => a.t - b.t);
  return {
    key: scenario.key,
    title: scenario.title,
    caption: scenario.caption,
    trouble: scenario.trouble,
    order,
    instanceId: id,
    startedAt: new Date(t0).toISOString(),
    events,
  };
}

/**
 * The run's steps as the contract's junction events, timed by each step's own start and end. SendToRestaurant's
 * attempts are the tablet's calls: an attempt that timed out is the step failing, then a retry, then the step
 * running again on the next attempt.
 */
function junctionEvents(at, steps, calls, run) {
  const events = [];
  for (const s of steps.sort((a, b) => a.position - b.position)) {
    if (!STEPS.includes(s.name)) throw new Error(`the run has a step ${s.name} the train lacks`);
    const status = { IN_PROGRESS: "running", COMPLETED: "completed", FAILED: "failed", CANCELLED: "failed" }[s.state];
    const failedMessage = s.state === "FAILED" && run.failureJunction === s.name ? run.failureReason : undefined;
    events.push({ t: at(s.startedAt), kind: "junction", attempt: 1, step: s.name, status: "running" });

    const attempts = s.name === "SendToRestaurant" ? calls : [];
    for (const call of attempts.filter((c) => c.problem && c.attempt < Math.max(...attempts.map((a) => a.attempt)))) {
      const next = attempts.find((c) => c.attempt === call.attempt + 1);
      events.push({ t: at(call.endedAt), kind: "junction", attempt: call.attempt, step: s.name, status: "failed", message: call.problem });
      const backoff = Math.round((Date.parse(next.startedAt) - Date.parse(call.endedAt)) / 100) / 10;
      events.push({ t: at(call.endedAt), kind: "retry", attempt: next.attempt, message: `Trying the tablet again in ${backoff} s (attempt ${next.attempt}).` });
      events.push({ t: at(next.startedAt), kind: "junction", attempt: next.attempt, step: s.name, status: "running" });
    }
    if (status === "running") continue;
    const attempt = attempts.length ? Math.max(...attempts.map((a) => a.attempt)) : 1;
    events.push({
      t: at(s.endedAt),
      kind: "junction",
      attempt,
      step: s.name,
      status,
      ...(status === "failed" ? { message: failedMessage ?? attempts.at(-1)?.problem ?? "The step failed." } : {}),
    });
  }
  return events;
}

/** The first DeliverOrderTrain run started since the order was placed: the run Ordered queued. */
async function deliveryRun(placedAt) {
  let found = null;
  await waitFor(async () => {
    found = (await post(ops.runs, { after: placedAt })).data.operations.executions.items[0]?.id ?? null;
    return found != null;
  }, 15_000, "the delivery run to be queued");
  return found;
}

async function waitFor(condition, timeoutMs, what) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await condition()) return;
    await sleep(POLL_MS);
  }
  throw new Error(`timed out waiting for ${what}`);
}

/** Exports the machine's IR with the trax CLI from the Recovery assembly, as shipped (no host setting applies). */
function exportIr() {
  const assembly = join(PROJECT, "bin", "Debug", "net10.0", "Trax.Samples.Recovery.dll");
  if (!existsSync(assembly)) throw new Error(`build the Recovery sample first: ${assembly} is missing`);
  mkdirSync(OUT, { recursive: true });
  execFileSync(
    "dotnet",
    [
      "run", "--project", join(REPO, "Trax.Cli", "src", "Trax.Cli"), "--",
      "machine", "generate",
      "--assembly", assembly,
      "--machine", "Trax.Samples.Recovery.Machines.OrderMachine",
      "--ir-out", OUT,
    ],
    { stdio: ["ignore", "ignore", "inherit"] },
  );
}

/** Shortens every idle stretch past IDLE_MS to COMPRESSED_GAP, marking the event after it with the real gap. */
function compress(events) {
  let shift = 0;
  let previous = null;
  return events.map((e) => {
    const real = e.t;
    const out = { ...e, t: real - shift };
    if (previous != null && real - previous > IDLE_MS) {
      const removed = real - previous - COMPRESSED_GAP;
      shift += removed;
      out.t = real - shift;
      out.gap = removed;
    }
    previous = real;
    return out;
  });
}

/**
 * The line to light for each part of the machine, read from its source. `state:Cart` and `state:Ordered` are their
 * m.In lines; an end state is the line that sends the order there (`state:Delivered` the .OnDone, `state:Cancelled`
 * the .OnFailed, `state:Cleared` the timer's .GoTo). `event:<trigger>` is its .On line, under the operation's name and
 * the trigger's; `invoke` the .Invokes line, `timer` the .After line, and `outcome:<done|failed|cancelled>` each outcome.
 */
function linesOf(code) {
  const lines = code.split("\n");
  const find = (needle) => {
    const index = lines.findIndex((l) => l.includes(needle));
    if (index < 0) throw new Error(`OrderMachine.cs has no line with ${needle}`);
    return index + 1;
  };
  const camel = (name) => name[0].toLowerCase() + name.slice(1);
  const out = {};
  for (const state of ["Cart", "Ordered"]) out[`state:${state}`] = find(`m.In(OrderState.${state})`);
  out["state:Delivered"] = find(".OnDone(OrderState.Delivered");
  out["state:Cancelled"] = find(".OnFailed(OrderState.Cancelled");
  out["state:Cleared"] = find(".GoTo(OrderState.Cleared");
  for (const trigger of ["AddItem", "PlaceOrder"]) {
    out[`event:${camel(trigger)}`] = find(`.On(OrderTrigger.${trigger})`);
    out[`event:${trigger}`] = out[`event:${camel(trigger)}`];
  }
  out["event:start"] = find(".Start<OrderStart>");
  out.invoke = find(".Invokes<IDeliverOrderTrain");
  out.timer = find(".After(");
  out["outcome:done"] = find(".OnDone(");
  out["outcome:failed"] = find(".OnFailed(");
  out["outcome:cancelled"] = find(".OnCancelled(");
  return out;
}

function websiteCopy() {
  const code = readFileSync(MACHINE_SOURCE, "utf8").replace(/\r/g, "");
  const scenarios = SCENARIOS.map((s) => {
    const raw = JSON.parse(readFileSync(join(OUT, `${s.key}.json`), "utf8"));
    return { key: raw.key, title: raw.title, caption: raw.caption, recordedAt: raw.startedAt, events: compress(raw.events) };
  });
  return {
    recordedAt: scenarios.map((s) => s.recordedAt).sort()[0],
    order: JSON.parse(readFileSync(join(OUT, `${SCENARIOS[0].key}.json`), "utf8")).order,
    // The cart's timer as the machine ships it, from its real IR; the recording's host shortened it.
    timerAfter: JSON.parse(readFileSync(join(OUT, "order.ir.json"), "utf8")).timers[0].after,
    source: { file: MACHINE_FILE, code },
    lines: linesOf(code),
    train: {
      name: "DeliverOrderTrain",
      steps: STEPS,
      // The train verbatim, which the page shows while the order's run is on its junctions.
      source: { file: TRAIN_FILE, code: readFileSync(TRAIN_SOURCE, "utf8").replace(/\r/g, "") },
    },
    scenarios,
  };
}

function writeCopies(copyTo) {
  const data = JSON.stringify(websiteCopy()) + "\n";
  const local = join(here, "out", "order-recordings.json");
  writeFileSync(local, data);
  console.log(`wrote ${local}`);
  if (copyTo) {
    mkdirSync(copyTo, { recursive: true });
    const target = join(copyTo, "order-recordings.json");
    writeFileSync(target, data);
    console.log(`wrote ${target}`);
  }
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  if (args.includes("--copy-only")) return writeCopies(copyTo);

  exportIr();
  const sockets = { laptop: socket(), phone: socket(), junctions: socket() };
  try {
    for (const scenario of SCENARIOS) {
      const recording = await record(scenario, sockets);
      writeFileSync(join(OUT, `${scenario.key}.json`), JSON.stringify(recording, null, 2));
      const states = recording.events.filter((e) => e.kind === "push" && e.screen === "laptop").map((e) => e.state);
      console.log(`recorded ${scenario.key}: ${recording.events.length} events, ${states.join(" -> ")}`);
    }
  } finally {
    await Promise.all(Object.values(sockets).map((s) => s.dispose()));
  }
  writeCopies(copyTo);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
