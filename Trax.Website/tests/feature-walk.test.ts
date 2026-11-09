// Holds the home page's features demo (src/lib/feature-walk.ts) to what Trax derived: every phrase is on the line it
// names, every state change between two moments is a move the derived machines make, and everything the screens show
// (labels, the refusal, the seat) is the glossary's or the IR's. Run with `npm test`. When the feature changes, derive
// it again, copy the files into src/data/feature-walk.json and fix whatever this then fails on.
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import data from "../src/data/feature-walk.json" with { type: "json" };
import { WALK, accessLines, derivedOf, featureLines, inFeature, type Ir, type WalkData } from "../src/lib/feature-walk.ts";

const walk = data as unknown as WalkData;
const bookingIr = walk.machines.booking.ir;
const flightIr = walk.machines.flight.ir;
const glossary = walk.glossary.glossary;
const line = (n: number) => walk.feature.text.split("\n")[n - 1];
const quoted = (n: number) => /"([^"]*)"/.exec(line(n))?.[1];

const sampleDir = fileURLToPath(new URL(`../../Trax.Samples/samples/${walk.sample}/`, import.meta.url));
test("the copied files are the sample's own", () => {
  assert.ok(existsSync(sampleDir), `samples/${walk.sample} is missing`);
  const read = (p: string) => readFileSync(sampleDir + p, "utf8");
  assert.equal(walk.feature.text, read(walk.feature.file));
  assert.equal(walk.access.text, read(walk.access.file));
  for (const m of ["booking", "flight"] as const) assert.deepEqual(walk.machines[m].ir, JSON.parse(read(walk.machines[m].file)));
  assert.deepEqual(walk.host.policies, JSON.parse(read(walk.host.file)).policies);
  assert.deepEqual(glossary, JSON.parse(read(walk.glossary.file)));
});

test("every phrase is on the line it names, and the page shows that line", () => {
  const shown = new Set(featureLines(walk).map((l) => l.n));
  for (const s of WALK) {
    for (const m of s.marks) {
      assert.ok(line(m.line).includes(inFeature(m.phrase)), `"${m.phrase}" is not on line ${m.line}: ${line(m.line)}`);
      assert.ok(shown.has(m.line), `line ${m.line} is not shown`);
    }
  }
});

// A change of state between two moments must be one the machine makes: a move, an outcome's edge, or the start.
function moves(ir: Ir, from: string | null, to: string | null): boolean {
  if (from === to) return true;
  if (from === null) return to === ir.initialState;
  if (ir.transitions.some((t) => t.from === from && t.to === to)) return true;
  return Object.values(ir.outcomes ?? {}).some((o) => o.state === from && o.edges.some((e) => e.to === to));
}

test("every state change from one moment to the next is a move the derived machines make", () => {
  let prev = { ada: null as string | null, flight: flightIr.initialState };
  for (const [i, s] of WALK.entries()) {
    for (const who of ["ada"] as const) {
      const now = s[who].state;
      if (now) assert.ok(bookingIr.states.includes(now), now);
      assert.ok(moves(bookingIr, prev[who], now), `moment ${i + 1}: ${who}'s booking cannot go from ${prev[who]} to ${now}`);
    }
    assert.ok(flightIr.states.includes(s.flight), s.flight);
    assert.ok(moves(flightIr, prev.flight, s.flight), `moment ${i + 1}: the flight cannot go from ${prev.flight} to ${s.flight}`);
    for (const p of s.lit) {
      if (p.startsWith("booking:")) assert.ok(bookingIr.states.includes(p.slice(8)), p);
      if (p.startsWith("flight:")) assert.ok(flightIr.states.includes(p.slice(7)), p);
    }
    prev = { ada: s.ada.state, flight: s.flight };
  }
});

test("every access line a moment lights or shows is in access.txt", () => {
  const lines = accessLines(walk);
  for (const s of WALK) {
    for (const p of s.lit) if (p.startsWith("access:")) assert.ok(lines.includes(p.slice(7)), p);
    if (s.derived.from === "access") for (const l of s.derived.lines) assert.ok(lines.includes(l), l);
  }
  for (const l of ["booking CheckIn: passenger", "booking GivePassport: passenger", "booking OpenCheckIn: the server only", "booking Miss: the server only", "flight OpenCheckIn: the server only", "flight CloseGate: the server only", "view bookings: passenger", "view departures: agent, passenger"])
    assert.ok(lines.includes(l), l);
});

test("a Booked booking has no check-in move, so the app offers none", () => {
  assert.deepEqual(bookingIr.transitions.filter((t) => t.from === "Booked").map((t) => t.trigger), ["OpenCheckIn"]);
});

test("checking in is guarded by the passport, and the refusal says what the feature says", () => {
  const checkIn = bookingIr.transitions.find((t) => t.trigger === "CheckIn");
  assert.equal(`${checkIn?.from} ${checkIn?.to}`, "Open Issuing");
  assert.deepEqual(checkIn?.guard, glossary.machines.booking.guards?.hasPassport.rule);
  assert.equal(checkIn?.guardMessage, quoted(29));
  const told = WALK.find((s) => s.ada.told);
  assert.equal(told?.ada.told, checkIn?.guardMessage);
});

test("giving a passport keeps the booking open and stores the number the feature gives", () => {
  const give = bookingIr.transitions.find((t) => t.trigger === "GivePassport");
  assert.equal(`${give?.from} ${give?.to}`, "Open Open");
  assert.deepEqual(give?.reduce, { reduce: "set", steps: [{ field: "passport", value: { input: "passport" } }] });
  assert.ok(WALK.some((s) => s.ada.passport === quoted(53)));
});

test("the two policies the walk lights are broadcasts from the flight", () => {
  const byEnters = Object.fromEntries(walk.host.policies.filter((p) => p.when.machine === "flight").map((p) => [p.when.enters, p]));
  assert.deepEqual(byEnters.Open, { shape: "broadcast", when: { machine: "flight", enters: "Open" }, then: { machine: "booking", inState: "Booked", trigger: "OpenCheckIn" } });
  assert.deepEqual(byEnters.Closed, { shape: "broadcast", when: { machine: "flight", enters: "Closed" }, then: { machine: "booking", inState: "Open", trigger: "Miss" } });
});

test("the boarding pass train's accepted outcome checks the booking in, and a turned-down one does not", () => {
  const inv = glossary.machines.booking.invokes?.Issuing;
  assert.equal(inv?.train, "Trax.Samples.CheckIn.Trains.IIssueBoardingPassTrain");
  const done = bookingIr.outcomes?.["Issuing.done"];
  const to = (accepted: boolean) => done?.edges.find((e) => (e.guard as { value?: unknown } | undefined)?.value === accepted)?.to;
  assert.equal(to(true), "CheckedIn");
  assert.equal(to(false), "TurnedDown");
});

test("checking in stores the seat the feature picks, and the bookings view shows it", () => {
  const checkIn = bookingIr.transitions.find((t) => t.trigger === "CheckIn");
  assert.deepEqual(checkIn?.reduce, { reduce: "set", steps: [{ field: "seat", value: { input: "seat" } }] });
  assert.equal(quoted(19), quoted(23));
  assert.ok(WALK.some((s) => s.ada.seat === quoted(19)));
});

test("the views' labels are the ones the feature expects", () => {
  const dep = glossary.views.departures.fields.status;
  assert.equal(dep.from, "flight.state");
  assert.equal(dep.labels?.Open, quoted(14));
  assert.equal(dep.labels?.Closed, quoted(49));
  const status = glossary.views.bookings.fields.status;
  assert.equal(status.labels?.Missed, quoted(48));
  assert.equal(glossary.views.bookings.fields.seat.from, "booking.seat");
});

test("every moment shows derived text", () => {
  for (const s of WALK) assert.ok(derivedOf(walk, s.derived).text.length > 4, s.says);
});
