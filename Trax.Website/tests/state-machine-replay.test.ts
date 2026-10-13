// Drives the recorded StateMachine sample through the landing page's player (src/lib/state-machine-replay.ts)
// and checks what it ends up showing. Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import data from "../src/data/state-machine-recordings.json" with { type: "json" };
import {
  answerOf,
  availability,
  configureRange,
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

test("opening a machine starts one, with the id the server minted", () => {
  for (const machine of machines) {
    const s = open(recordings, machine);
    assert.deepEqual(s.log.map((e) => [e.exchange.operation, e.kind]), [["start", "start"]]);
    assert.equal(s.log[0].problem, null);
    assert.equal(answerOf(s.log[0].exchange).answer.id, recordings.machines[machine].id);
    assert.equal(s.log[0].snapshot?.state, snapshotOf(recordings, s).state);
  }
  assert.equal(snapshotOf(recordings, open(recordings, "checkout")).state, "Cart");
});

test("every request is one of the machine's own operations, and none sends a snapshot", () => {
  for (const machine of machines) {
    const m = recordings.machines[machine];
    const exchanges = [...m.opening, ...Object.values(m.edges).flatMap((edges) => Object.values(edges).map((e) => e.exchange))];
    for (const exchange of exchanges) {
      assert.match(exchange.query, new RegExp(`^mutation \\{ machines \\{ ${machine} \\{ ${exchange.operation}\\(`));
      assert.doesNotMatch(exchange.query, /snapshot|dispatch/i);
      assert.equal(answerOf(exchange).machine, machine);
    }
  }
});

test("the turnstile takes a quarter, refuses a penny, and refuses Push while locked", () => {
  const penny = pressAll("turnstile", ["coin-penny"]);
  assert.equal(penny.log.at(-1)!.problem?.code, "guard-failed");
  assert.equal(penny.log.at(-1)!.kind, "refused");
  assert.equal(snapshotOf(recordings, penny).state, "Locked");

  const pushed = pressAll("turnstile", ["push"]);
  assert.equal(pushed.log.at(-1)!.problem?.code, "no-transition");

  const coinAgain = pressAll("turnstile", ["coin-dollar", "coin-quarter"]);
  assert.equal(coinAgain.log.at(-1)!.problem?.code, "no-transition");
  assert.equal(snapshotOf(recordings, coinAgain).context.paidWith, "dollar");

  const through = pressAll("turnstile", ["coin-quarter", "push"]);
  assert.equal(snapshotOf(recordings, through).state, "Locked");
  assert.equal(through.log.at(-2)!.snapshot?.context.paidWith, "quarter");
  assert.deepEqual(through.log.at(-1)!.snapshot?.context, {});
});

test("Pay twice takes one charge, and the second returns the first one's receipt", () => {
  const paid = pressAll("checkout", ["add-item", "next", "pay"]);
  const first = paid.log.at(-1)!;
  assert.equal(first.snapshot?.state, "Paid");
  assert.equal(first.kind, "charge");
  assert.equal(paid.charges.length, 1);
  assert.equal(paid.charges[0].receipt, first.snapshot?.context.receipt);
  assert.equal(paid.charges[0].amountCents, 999);

  const again = press(recordings, paid, "pay");
  const second = again.log.at(-1)!;
  assert.ok(second.replayed);
  assert.equal(second.kind, "replay");
  assert.equal(second.snapshot?.context.receipt, first.snapshot?.context.receipt);
  assert.equal(again.charges.length, 1, "a second Pay charged again");
});

test("the server refuses what the page should not be able to do", () => {
  assert.equal(pressAll("checkout", ["pay"]).log.at(-1)!.problem?.code, "no-transition");
  assert.equal(pressAll("checkout", ["next"]).log.at(-1)!.problem?.code, "guard-failed");
  assert.equal(pressAll("checkout", ["add-item", "next", "add-item"]).log.at(-1)!.problem?.code, "no-transition");
  const paid = pressAll("checkout", ["add-item", "next", "pay", "back"]);
  assert.equal(paid.log.at(-1)!.problem?.code, "no-transition");
  assert.equal(snapshotOf(recordings, paid).state, "Paid");
  assert.equal(paid.charges.length, 1);
});

test("the server prices the cart: each item adds $9.99 to the total", () => {
  const s = pressAll("checkout", ["add-item", "add-item"]);
  assert.deepEqual(snapshotOf(recordings, s).context, { items: ["book", "pen"], total: 1998 });
  assert.equal(availability(recordings, s, "add-item").ok, false);
});

test("Reset after paying starts a new cart, and paying it again is a second charge", () => {
  const s = pressAll("checkout", ["add-item", "next", "pay", "reset", "add-item", "add-item", "next", "pay"]);
  assert.equal(s.charges.length, 2);
  assert.deepEqual(s.charges.map((c) => c.amountCents), [1998, 999]);
  assert.notEqual(s.charges[0].receipt, s.charges[1].receipt);
  const reset = pressAll("checkout", ["add-item", "next", "pay", "reset"]);
  assert.deepEqual(snapshotOf(recordings, reset).context, { items: [], total: 0 });
  assert.equal(availability(recordings, pressAll("checkout", ["add-item", "next", "pay", "reset", "add-item", "next", "pay", "reset", "add-item", "next"]), "pay").ok, false);
});

test("the code panel points into the machine's Configure for every recorded answer", () => {
  const code = recordings.source.code;
  for (const machine of machines) {
    const [start, end] = configureRange(code, machine);
    const opened = open(recordings, machine).log[0];
    assert.ok(lineOf(code, machine, opened) >= start, `${machine} start`);
    for (const [node, edges] of Object.entries(recordings.machines[machine].edges))
      for (const action of Object.keys(edges)) {
        const s = press(recordings, { machine, node, log: [], charges: [] }, action);
        const line = lineOf(code, machine, s.log.at(-1));
        assert.ok(line >= start && line < end, `${machine} ${node} ${action}`);
      }
  }
});

test("the code panel points at the line that decided", () => {
  const code = recordings.source.code;
  const lines = code.split("\n");
  const at = (machine: MachineName, ids: string[]) => lines[lineOf(code, machine, pressAll(machine, ids).log.at(-1))].trim();
  assert.match(at("turnstile", ["coin-penny"]), /^\.When\(Input\(\(CoinInput/);
  assert.match(at("turnstile", ["push"]), /^m\.In\(TurnstileState\.Locked\)/);
  assert.match(at("turnstile", ["coin-quarter"]), /^\.On\(TurnstileTrigger\.Coin\)/);
  assert.match(at("checkout", ["next"]), /^\.When\(HasItems\)/);
  assert.match(at("checkout", ["add-item", "next", "pay"]), /^\.RunsOnce<ICharge>/);
  assert.match(at("checkout", ["add-item", "next", "pay", "pay"]), /^\.RunsOnce<ICharge>/);
  assert.match(at("checkout", ["add-item", "next", "pay", "reset"]), /^\.On\(CheckoutTrigger\.Reset\)/);
});
