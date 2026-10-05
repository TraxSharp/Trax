// Drives the recorded StateMachine sample through the landing page's player (src/lib/state-machine-replay.ts)
// and checks what it ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/state-machine-recordings.json" with { type: "json" };
import {
  availability,
  lineOf,
  open,
  press,
  snapshotOf,
  type MachineName,
  type Recordings,
  type Session,
} from "../src/lib/state-machine-replay.ts";

const recordings = data as unknown as Recordings;
const machines: MachineName[] = ["turnstile", "checkout"];

const pressAll = (machine: MachineName, ids: string[]): Session =>
  ids.reduce((s, id) => {
    assert.ok(availability(recordings, s, id).ok, `${id} is not available in ${s.node}`);
    return press(recordings, s, id);
  }, open(recordings, machine));

test("every button the page offers, from every state it can reach, has a recorded answer or says why not", () => {
  for (const machine of machines) {
    const m = recordings.machines[machine];
    const seen = new Set([m.start]);
    const queue = [m.start];
    while (queue.length) {
      const node = queue.shift()!;
      const session: Session = { machine, node, log: [], charges: [] };
      for (const action of recordings.actions[machine]) {
        const can = availability(recordings, session, action.id);
        if (!can.ok) {
          assert.ok(can.reason && can.reason !== "Not recorded.", `${machine} ${node}: ${action.id} has no answer`);
          continue;
        }
        const to = m.edges[node][action.id].to;
        assert.ok(m.nodes[to], `${machine} ${node}: ${action.id} leads nowhere`);
        if (!seen.has(to)) {
          seen.add(to);
          queue.push(to);
        }
      }
    }
    assert.equal(seen.size, Object.keys(m.nodes).length, `${machine} has states no button reaches`);
  }
});

test("opening a machine loads, finds no draft and saves a first one", () => {
  const s = open(recordings, "checkout");
  assert.deepEqual(s.log.map((e) => e.exchange.op), ["loadSnapshot", "saveSnapshot"]);
  assert.equal(s.log[0].problem?.code, "not-found");
  assert.equal(snapshotOf(recordings, s).state, "Cart");
});

test("the turnstile takes a quarter, refuses a penny, and refuses Push while locked", () => {
  const penny = pressAll("turnstile", ["coin-penny"]);
  assert.equal(penny.log.at(-1)!.problem?.code, "guard-failed");
  assert.equal(snapshotOf(recordings, penny).state, "Locked");

  const pushed = pressAll("turnstile", ["push"]);
  assert.equal(pushed.log.at(-1)!.problem?.code, "no-transition");

  const through = pressAll("turnstile", ["coin-quarter", "push"]);
  assert.equal(snapshotOf(recordings, through).state, "Locked");
  assert.equal(through.log.at(-2)!.snapshot?.context.paidWith, "quarter");
});

test("Pay twice takes one charge, and the second returns the first one's receipt", () => {
  const paid = pressAll("checkout", ["add-item", "next", "pay"]);
  const first = paid.log.at(-1)!;
  assert.equal(first.snapshot?.state, "Paid");
  assert.equal(paid.charges.length, 1);
  assert.equal(paid.charges[0].receipt, first.snapshot?.context.receipt);
  assert.equal(paid.charges[0].amountCents, 999);

  const again = press(recordings, paid, "pay");
  const second = again.log.at(-1)!;
  assert.ok(second.replayed);
  assert.equal(second.snapshot?.context.receipt, first.snapshot?.context.receipt);
  assert.equal(again.charges.length, 1, "a second Pay charged again");
});

test("the server refuses what the page should not be able to do", () => {
  assert.equal(pressAll("checkout", ["pay"]).log.at(-1)!.problem?.code, "no-transition");
  assert.equal(pressAll("checkout", ["next"]).log.at(-1)!.problem?.code, "invalid-context");
  assert.equal(pressAll("checkout", ["add-item", "save-penny-total"]).log.at(-1)!.problem?.code, "invalid-context");
  const paid = pressAll("checkout", ["add-item", "next", "pay", "back"]);
  assert.equal(paid.log.at(-1)!.problem?.code, "no-transition");
  assert.equal(snapshotOf(recordings, paid).state, "Paid");
});

test("Reset after paying starts a new cart, and paying it again is a second charge", () => {
  const s = pressAll("checkout", ["add-item", "next", "pay", "reset", "add-item", "add-item", "next", "pay"]);
  assert.equal(s.charges.length, 2);
  assert.deepEqual(s.charges.map((c) => c.amountCents), [1998, 999]);
  assert.notEqual(s.charges[0].receipt, s.charges[1].receipt);
  assert.equal(availability(recordings, pressAll("checkout", ["add-item", "next", "pay", "reset", "add-item", "next", "pay", "reset", "add-item", "next"]), "pay").ok, false);
});

test("the code panel points into the machine's Configure for every recorded answer", () => {
  const code = recordings.source.code;
  for (const machine of machines)
    for (const [node, edges] of Object.entries(recordings.machines[machine].edges))
      for (const action of Object.keys(edges)) {
        const s = press(recordings, { machine, node, log: [], charges: [] }, action);
        assert.ok(lineOf(code, machine, s.log.at(-1)) >= 0, `${machine} ${node} ${action}`);
      }
});
