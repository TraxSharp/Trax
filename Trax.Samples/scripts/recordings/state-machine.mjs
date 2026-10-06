// Records the StateMachine sample for traxsharp.net, which lets a reader drive both machines on its landing page.
// Start the sample's host as its README says (the web page is not needed), then run
//
//   node state-machine.mjs [--copy-to <dir>]
//
// The script presses every button the sample's page offers, from every state the page can reach, over the same
// GraphQL operations the page sends, and writes down each request and the server's whole response. It walks each
// machine as a graph: a node is a draft's state and context, an edge is one button pressed there. Each edge is
// recorded on a fresh draft brought to its node by replaying the path that first reached it, so every response
// is the one the server gives at that point. The page's free-form draft editor becomes two presets: add the next
// item of a fixed list (at most two), and save with a $0.01 total.
//
// The checkout's charge is the one thing a replay repeats: getting a draft back to Paid means paying again. So a
// Paid draft's buttons are all pressed on the one draft that was paid on the way there (the refusals and a second
// Pay first, Reset last), the Pay that reached it is recorded on that same draft, and the payment provider's
// charges are recorded as the ones each send added (payments.listCharges before and after it). A checkout is paid
// at most twice per run of the page.
//
// out/state-machine/<machine>.json holds each machine's graph. --copy-to also writes <dir>/state-machine-recordings.json,
// the website's copy, with Machines.cs; --copy-only skips recording. Each draft's id is written as the page's own
// id for that machine (the recorder needs a fresh draft per path, the page has one), and nothing else is changed.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const HOST = process.env.STATE_MACHINE_HOST ?? "localhost:5280";
const API_KEY = "alice-key-do-not-use-in-production";
const OUT = join(here, "out", "state-machine");
const MAX_ITEMS = 2;
const MAX_PAID = 2;
const ITEMS = ["book", "pen"];
const UNIT_PRICE_CENTS = 999;

const OUTPUT = "externalId output { snapshot problem { code message } }";
const INPUT_TYPES = {
  saveSnapshot: "SaveSnapshotInput",
  advanceSnapshot: "AdvanceSnapshotInput",
  loadSnapshot: "LoadSnapshotInput",
  sendSnapshot: "SendSnapshotInput",
};
// The page's own documents (web/src/traxTransport.ts).
const QUERIES = Object.fromEntries(
  Object.entries(INPUT_TYPES).map(([op, type]) => [
    op,
    `mutation($i: ${type}!){ dispatch { stateMachine { ${op}(input:$i){ ${OUTPUT} } } } }`,
  ]),
);
const LIST_CHARGES = "{ discover { payments { listCharges { charges { receipt amountCents items chargedAt } } } } }";

// The page's two machines, their ids, first drafts and buttons (web/src/machines.ts and App.tsx).
const MACHINES = {
  turnstile: {
    id: "a0000000-0000-0000-0000-000000000001",
    initial: { machine: "turnstile", version: 1, state: "Locked", context: {} },
    actions: [
      { id: "coin-penny", kind: "advance", trigger: "Coin", input: { coin: "penny" } },
      { id: "coin-quarter", kind: "advance", trigger: "Coin", input: { coin: "quarter" } },
      { id: "coin-dollar", kind: "advance", trigger: "Coin", input: { coin: "dollar" } },
      { id: "push", kind: "advance", trigger: "Push" },
    ],
  },
  checkout: {
    id: "b0000000-0000-0000-0000-000000000002",
    initial: { machine: "checkout", version: 2, state: "Cart", context: { items: [], receipt: null, total: 0 } },
    // Ordered so that on a Paid draft every button that leaves it unchanged comes before Reset.
    actions: [
      { id: "save-penny-total", kind: "save", preset: "penny-total" },
      { id: "add-item", kind: "save", preset: "add-item" },
      { id: "back", kind: "advance", trigger: "Back" },
      { id: "next", kind: "advance", trigger: "Next" },
      { id: "pay", kind: "send" },
      { id: "reset", kind: "advance", trigger: "Reset" },
    ],
  },
};

let requestCounter = 0;

async function post(query, variables) {
  const started = performance.now();
  const response = await fetch(`http://${HOST}/trax/graphql`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": API_KEY },
    body: JSON.stringify({ query, variables }),
  });
  const body = await response.json();
  if (body.errors?.length) throw new Error(`${body.errors[0].message} (${query.slice(0, 60)}...)`);
  return { body, ms: Math.round(performance.now() - started) };
}

async function charges() {
  const { body } = await post(LIST_CHARGES);
  return body.data.discover.payments.listCharges.charges;
}

/** One of the four mutations, as the page sends it, with the server's whole answer. */
async function mutate(op, input) {
  const { body, ms } = await post(QUERIES[op], { i: input });
  const raw = body.data.dispatch.stateMachine[op];
  return {
    exchange: { op, variables: { i: input }, response: body, ms },
    snapshot: raw.output.snapshot ? JSON.parse(raw.output.snapshot) : null,
    problem: raw.output.problem,
  };
}

/** What a button sends, given the draft as the page last saw it. Null when the page would not offer it. */
function request(machine, action, draftId, current) {
  const name = current.machine;
  switch (action.kind) {
    case "advance":
      return [
        "advanceSnapshot",
        { machine: name, id: draftId, trigger: action.trigger, input: action.input ? JSON.stringify(action.input) : null, requestId: null },
      ];
    case "send":
      return ["sendSnapshot", { machine: name, id: draftId, requestId: `pay-${++requestCounter}` }];
    case "save": {
      const items = current.context.items ?? [];
      // The page saves with the state it shows and no receipt (web/src/App.tsx, DraftEditor).
      if (action.preset === "add-item") {
        if (items.length >= MAX_ITEMS) return null;
        const next = [...items, ITEMS[items.length]];
        return ["saveSnapshot", save(name, draftId, current.state, next, next.length * UNIT_PRICE_CENTS)];
      }
      if (items.length === 0) return null; // the page disables "Save with a $0.01 total" on an empty cart
      return ["saveSnapshot", save(name, draftId, current.state, items, 1)];
    }
  }
}

function save(machine, id, state, items, total) {
  return {
    machine,
    id,
    snapshot: JSON.stringify({ machine, version: 2, state, context: { items, receipt: null, total } }),
  };
}

/**
 * A node: the draft's state and context, with a receipt counted rather than named (each payment has its own),
 * and for the checkout how many times it was paid, so the page's charge list is part of where it is.
 */
function keyOf(snapshot, paid) {
  const context = { ...snapshot.context };
  if (context.receipt) context.receipt = "receipt";
  return `${snapshot.state} ${JSON.stringify(context)}${snapshot.machine === "checkout" ? ` paid:${paid}` : ""}`;
}

/** Presses one button on a draft and records what happened. */
async function press(machine, action, draft) {
  const req = request(machine, action, draft.id, draft.snapshot);
  if (!req) return null;
  const before = action.kind === "send" ? await charges() : null;
  const result = await mutate(...req);
  const added = before
    ? (await charges()).filter((c) => !before.some((b) => b.receipt === c.receipt))
    : [];
  const snapshot = result.snapshot ?? draft.snapshot;
  const paid = draft.paid + added.length;
  return { exchange: result.exchange, problem: result.problem, snapshot, paid, charges: added };
}

/** A fresh draft opened the way the page opens one (load, then save the first draft), brought along a path. */
async function open(machine) {
  const m = MACHINES[machine];
  const id = randomUUID();
  const load = await mutate("loadSnapshot", { machine, id });
  const first = await mutate("saveSnapshot", { machine, id, snapshot: JSON.stringify(m.initial) });
  return { draft: { id, snapshot: first.snapshot, paid: 0 }, opening: [load.exchange, first.exchange] };
}

async function walk(draft, machine, path) {
  let last = null;
  for (const actionId of path) {
    const action = MACHINES[machine].actions.find((a) => a.id === actionId);
    last = await press(machine, action, draft);
    draft.snapshot = last.snapshot;
    draft.paid = last.paid;
  }
  return last;
}

async function record(machine) {
  const m = MACHINES[machine];
  const { draft: root, opening } = await open(machine);
  const startKey = keyOf(root.snapshot, 0);
  const nodes = new Map([[startKey, { path: [], snapshot: root.snapshot, paid: 0 }]]);
  const edges = {};
  const queue = [startKey];
  const normalize = (draftId, value) => JSON.parse(JSON.stringify(value).replaceAll(draftId, m.id));

  while (queue.length) {
    const key = queue.shift();
    const node = nodes.get(key);
    edges[key] = {};
    let draft = null;

    for (const action of m.actions) {
      // Pay from Review would be a payment past the cap: the page offers it, the recording stops there.
      if (action.id === "pay" && node.snapshot.state === "Review" && node.paid >= MAX_PAID) continue;
      if (!request(machine, action, "x", node.snapshot)) continue;

      if (!draft) {
        draft = (await open(machine)).draft;
        const entry = await walk(draft, machine, node.path);
        // A paid draft's own Pay is the one recorded for the edge into it, so its receipt is the one shown.
        if (entry && node.snapshot.state === "Paid" && node.path.at(-1) === "pay") {
          const parent = node.parent;
          edges[parent].pay = { ...edges[parent].pay, exchange: normalize(draft.id, entry.exchange), charges: entry.charges };
          node.snapshot = entry.snapshot;
        }
      }

      const result = await press(machine, action, draft);
      const to = keyOf(result.snapshot, result.paid);
      edges[key][action.id] = { exchange: normalize(draft.id, result.exchange), to, charges: result.charges };
      if (to !== key) {
        if (node.snapshot.state === "Paid" && action.id !== m.actions.at(-1).id)
          throw new Error(`${machine}: ${action.id} moved a Paid draft before Reset; a replay would pay again`);
        if (!nodes.has(to)) {
          nodes.set(to, { path: [...node.path, action.id], snapshot: result.snapshot, paid: result.paid, parent: key });
          queue.push(to);
        }
        draft = null; // the next button starts from this node again
      }
    }
    console.log(`  ${machine}: ${key} (${Object.keys(edges[key]).length} buttons)`);
  }

  return {
    machine,
    id: m.id,
    start: startKey,
    opening: opening.map((e) => normalize(root.id, e)),
    nodes: Object.fromEntries([...nodes].map(([k, n]) => [k, { snapshot: n.snapshot, paid: n.paid }])),
    edges,
  };
}

function writeWebsiteCopy(dir) {
  const machines = Object.fromEntries(
    Object.keys(MACHINES).map((name) => [name, JSON.parse(readFileSync(join(OUT, `${name}.json`), "utf8"))]),
  );
  const project = resolve(here, "../../samples/StateMachine/Trax.Samples.StateMachine");
  const file = "Trax.Samples.StateMachine/Machines.cs";
  const data = {
    queries: { ...QUERIES, listCharges: LIST_CHARGES },
    actions: Object.fromEntries(Object.entries(MACHINES).map(([name, m]) => [name, m.actions])),
    maxItems: MAX_ITEMS,
    maxPaid: MAX_PAID,
    source: { file, code: readFileSync(join(project, "Machines.cs"), "utf8").replace(/\r/g, "") },
    machines,
  };
  mkdirSync(dir, { recursive: true });
  const target = join(dir, "state-machine-recordings.json");
  writeFileSync(target, JSON.stringify(data) + "\n");
  console.log(`wrote ${target}`);
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  if (!args.includes("--copy-only")) {
    mkdirSync(OUT, { recursive: true });
    for (const machine of Object.keys(MACHINES)) {
      const graph = await record(machine);
      writeFileSync(join(OUT, `${machine}.json`), JSON.stringify(graph, null, 2));
      console.log(`recorded ${machine}: ${Object.keys(graph.nodes).length} states`);
    }
  } else if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
  if (copyTo) writeWebsiteCopy(copyTo);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
