// The home page's features demo: three scenarios of a flight check-in feature, walked a step at a time on the two
// machines Trax derived from it, a passenger's booking and the flight. src/data/feature-walk.json holds verbatim
// copies of the feature, the glossary and what `trax machine from-gherkin` wrote; tests/feature-walk.test.ts checks
// every line, phrase and part this file names against them, and the copies against the sample.
//
// Each step colours one phrase of its line by what it becomes, and lights the part of a machine it produced in the
// same colour. What a step means follows Trax.Docs/reference/feature-files.md, "The steps" and "How a scenario is read".

export type Kind = "who" | "command" | "state" | "guard" | "train" | "view" | "policy";

export const KINDS: Record<Kind, { label: string; color: string }> = {
  who: { label: "who may do it", color: "#7a9ec2" },
  command: { label: "a command", color: "#d4a24c" },
  state: { label: "a state", color: "#6cc596" },
  guard: { label: "a guard", color: "#a093c6" },
  train: { label: "the boarding pass train", color: "#c97fae" },
  view: { label: "a view", color: "#5fb3a6" },
  policy: { label: "a policy: one machine moving another", color: "#e0805a" },
};

/** A part of the diagram, a line of access.txt, or a screen, that a step can light. */
export type Part =
  | `booking:${string}`
  | `flight:${string}`
  | "edge:b:OpenCheckIn"
  | "edge:b:CheckIn"
  | "edge:b:GivePassport"
  | "edge:b:Miss"
  | "edge:b:issued"
  | "edge:b:notIssued"
  | "guard:CheckIn"
  | "invoke:Issuing"
  | "edge:f:OpenCheckIn"
  | "edge:f:CloseGate"
  | "policy:open"
  | "policy:close"
  | `access:${string}`
  | "view:bookings"
  | "view:departures";

export type Derived =
  | { from: "access"; lines: string[] }
  | { from: "ir"; machine: "booking" | "flight"; pick: `from:${string}` | `transition:${string}` | `outcome:${string}` }
  | { from: "glossary"; pick: `invokes:${string}` | `view:${string}` }
  | { from: "host"; pick: `policy:${string}` };

/** A phrase of one line of the feature, and what it becomes. */
export interface Mark {
  line: number;
  phrase: string;
  kind: Kind;
}

/** What one passenger's app shows: their booking's state, what they typed, what the server told them. */
export interface Passenger {
  state: string | null;
  passport: string;
  /** The seat the booking holds, which checking in sets. */
  seat: string | null;
  told: string | null;
}

export interface WalkStep {
  /** The phrases this moment acts out, the first one's kind the moment's colour. */
  marks: Mark[];
  /** Who acts: Ada, or the server. */
  actor: "Ada" | "server";
  lit: Part[];
  ada: Passenger;
  flight: string;
  says: string;
  derived: Derived;
}

/** The colour a moment is shown in: its first phrase's. */
export const kindOf = (s: WalkStep): Kind => s.marks[0].kind;

const p = (state: string | null, passport = "", told: string | null = null, seat: string | null = null): Passenger => ({
  state,
  passport,
  seat,
  told,
});
const SEAT = "14C";
const PASSPORT = "X1234567";
const TOLD = "Add your passport number first.";

// One story through the feature: each moment is a line or two of a scenario, in an order the machines allow, ending
// with Ada's boarding pass. The gate scenario is in the feature but not in the story: Ada checks in, so she cannot also
// miss the flight.
export const WALK: WalkStep[] = [
  {
    marks: [
      { line: 6, phrase: "is a passenger", kind: "who" },
    ],
    actor: "server",
    lit: ["access:@passenger: roles passenger"],
    ada: p(null),
    flight: "Scheduled",
    says: "Ada signs in as a passenger: anyone with the passenger role.",
    derived: { from: "access", lines: ["@passenger: roles passenger"] },
  },
  {
    marks: [
      { line: 7, phrase: "has a booking", kind: "command" },
    ],
    actor: "Ada",
    lit: ["access:booking start: passenger", "booking:Booked"],
    ada: p("Booked"),
    flight: "Scheduled",
    says: "She has a booking of her own on the flight, which starts Booked.",
    derived: { from: "access", lines: ["booking start: passenger"] },
  },
  {
    marks: [
      { line: 57, phrase: 'checks in for her booking in seat "14C"', kind: "command" },
      { line: 58, phrase: "is still booked", kind: "state" },
    ],
    actor: "Ada",
    lit: ["booking:Booked"],
    ada: p("Booked"),
    flight: "Scheduled",
    says: "A Booked booking has no check-in move yet, so Ada's app offers none, and the server would refuse one.",
    derived: { from: "ir", machine: "booking", pick: "from:Booked" },
  },
  {
    marks: [
      { line: 11, phrase: "check-in opens for the flight", kind: "command" },
      { line: 12, phrase: "is open for check-in", kind: "state" },
    ],
    actor: "server",
    lit: ["edge:f:OpenCheckIn", "flight:Open", "access:flight OpenCheckIn: the server only"],
    ada: p("Booked"),
    flight: "Open",
    says: "The server opens check-in on the flight. No scenario shows a person doing it, so no person may.",
    derived: { from: "access", lines: ["flight OpenCheckIn: the server only"] },
  },
  {
    marks: [{ line: 13, phrase: "is open for check-in", kind: "policy" }],
    actor: "server",
    lit: ["policy:open", "edge:b:OpenCheckIn", "booking:Open"],
    ada: p("Open"),
    flight: "Open",
    says: "Ada's booking opens with the flight. That Then made a policy: when the flight opens, every Booked booking opens.",
    derived: { from: "host", pick: "policy:Open" },
  },
  {
    marks: [{ line: 14, phrase: 'show status "Check-in open"', kind: "view" }],
    actor: "server",
    lit: ["view:departures"],
    ada: p("Open"),
    flight: "Open",
    says: "The departures view shows the flight's state with the glossary's label, to every passenger.",
    derived: { from: "glossary", pick: "view:departures" },
  },
  {
    marks: [
      { line: 27, phrase: "has no passport number", kind: "guard" },
      { line: 28, phrase: 'checks in for her booking in seat "14C"', kind: "command" },
      { line: 29, phrase: "\u201cAdd your passport number first.\u201d", kind: "guard" },
    ],
    actor: "Ada",
    lit: ["guard:CheckIn", "edge:b:CheckIn", "access:booking CheckIn: passenger"],
    ada: p("Open", "", TOLD),
    flight: "Open",
    says: "Ada checks in with no passport number. The guard fails, so the server refuses and says why.",
    derived: { from: "ir", machine: "booking", pick: "transition:CheckIn" },
  },
  {
    marks: [{ line: 53, phrase: `gives passport "${PASSPORT}" for her booking`, kind: "command" }],
    actor: "Ada",
    lit: ["edge:b:GivePassport", "access:booking GivePassport: passenger"],
    ada: p("Open", PASSPORT),
    flight: "Open",
    says: "She gives her passport number. The booking stays open for check-in and keeps the number.",
    derived: { from: "ir", machine: "booking", pick: "transition:GivePassport" },
  },
  {
    marks: [
      { line: 18, phrase: "has a passport number", kind: "guard" },
      { line: 19, phrase: 'checks in for her booking in seat "14C"', kind: "command" },
      { line: 20, phrase: "is getting a boarding pass", kind: "state" },
    ],
    actor: "Ada",
    lit: ["guard:CheckIn", "edge:b:CheckIn", "booking:Issuing", "invoke:Issuing"],
    ada: p("Issuing", PASSPORT, null, SEAT),
    flight: "Open",
    says: "Now the guard holds. The booking keeps the seat she picked, and the server runs the boarding pass train on the passport.",
    derived: { from: "glossary", pick: "invokes:Issuing" },
  },
  {
    marks: [
      { line: 21, phrase: "the boarding pass is issued", kind: "train" },
      { line: 22, phrase: "is checked in", kind: "state" },
    ],
    actor: "server",
    lit: ["edge:b:issued", "invoke:Issuing", "booking:CheckedIn"],
    ada: p("CheckedIn", PASSPORT, null, SEAT),
    flight: "Open",
    says: "The train accepts the passport. That outcome checks the booking in; a passport it turns down goes elsewhere.",
    derived: { from: "ir", machine: "booking", pick: "outcome:Issuing.done" },
  },
  {
    marks: [{ line: 23, phrase: 'show seat "14C"', kind: "view" }],
    actor: "Ada",
    lit: ["view:bookings"],
    ada: p("CheckedIn", PASSPORT, null, SEAT),
    flight: "Open",
    says: "Ada's bookings view shows the seat her booking holds.",
    derived: { from: "glossary", pick: "view:seat" },
  },
];

export interface Ir {
  id: string;
  initialState: string;
  initialContext?: Record<string, unknown>;
  states: string[];
  transitions: { from: string; to: string; trigger: string; guard?: unknown; guardMessage?: string; reduce?: unknown }[];
  outcomes?: Record<string, { state: string; outcome: string; edges: { to: string; guard?: unknown; reduce?: unknown }[] }>;
}

export interface GlossaryMachine {
  states: Record<string, string | { phrase: string }>;
  triggers: Record<string, { phrase: string }>;
  guards?: Record<string, { holds: string; fails: string; rule: unknown }>;
  invokes?: Record<string, { train: string; input: Record<string, string>; output?: unknown; examples?: Record<string, Record<string, unknown>> }>;
  start?: string;
  initial: string;
  system?: string;
  name?: string;
}

export interface Policy {
  shape: string;
  when: { machine: string; enters: string };
  then: { machine: string; trigger: string; inState?: string };
}

export interface WalkData {
  sample: string;
  feature: { file: string; text: string };
  access: { file: string; text: string };
  machines: Record<"booking" | "flight", { file: string; ir: Ir }>;
  host: { file: string; policies: Policy[] };
  glossary: {
    file: string;
    glossary: {
      actors: Record<string, { noun: string; roles: string[] }>;
      machines: Record<"booking" | "flight", GlossaryMachine>;
      views: Record<string, { reads: unknown; fields: Record<string, { from: string; labels?: Record<string, string> }> }>;
    };
  };
}

/** The feature's own spelling of a phrase. */
export function inFeature(phrase: string): string {
  return phrase.replace(/[“”]/g, '"');
}

/** The derived text a step is read from: the file it is in and the lines to show. */
export function derivedOf(data: WalkData, d: Derived): { file: string; text: string } {
  if (d.from === "access") return { file: data.access.file, text: d.lines.join("\n") };
  if (d.from === "host") {
    const enters = d.pick.slice("policy:".length);
    const p = data.host.policies.find((x) => x.when.machine === "flight" && x.when.enters === enters);
    return { file: data.host.file, text: p ? json(p as unknown as Record<string, unknown>) : "" };
  }
  if (d.from === "ir") {
    const { file, ir } = data.machines[d.machine];
    if (d.pick.startsWith("from:")) {
      const from = d.pick.slice("from:".length);
      const moves = ir.transitions.filter((x) => x.from === from).map((x) => `${x.trigger} \u2192 ${x.to}`);
      return { file, text: `moves from ${from}:\n${moves.map((m) => `  ${m}`).join("\n")}` };
    }
    if (d.pick.startsWith("transition:")) {
      const t = ir.transitions.find((x) => x.trigger === d.pick.slice("transition:".length));
      return { file, text: t ? json(t) : "" };
    }
    const o = ir.outcomes?.[d.pick.slice("outcome:".length)];
    return { file, text: o ? json({ state: o.state, outcome: o.outcome, edges: o.edges }) : "" };
  }
  const file = data.glossary.file;
  const booking = data.glossary.glossary.machines.booking;
  const views = data.glossary.glossary.views;
  const [kind, name] = d.pick.split(":");
  if (kind === "invokes") {
    const inv = booking.invokes?.[name];
    return { file, text: inv ? json({ train: inv.train, input: inv.input, output: inv.output }) : "" };
  }
  if (name === "departures") return { file, text: json({ departures: views.departures }) };
  if (name === "seat") return { file, text: json({ seat: views.bookings?.fields.seat }) };
  const status = views.bookings?.fields.status;
  return { file, text: json({ status: { from: status?.from, labels: { Missed: status?.labels?.Missed } } }) };
}

// Short JSON: one key per line at the top, objects inline.
function json(value: Record<string, unknown>): string {
  const inner = Object.entries(value).map(([k, v]) => `  ${JSON.stringify(k)}: ${JSON.stringify(v)}`);
  return `{\n${inner.join(",\n")}\n}`;
}

/** Every line of access.txt, in the file's order. */
export function accessLines(data: WalkData): string[] {
  return data.access.text.split("\n").filter((l) => l.trim() !== "");
}

/**
 * The feature's lines to show: the header and Background, and every scenario a moment acts out. The scenarios the
 * story does not use (the plain moves and the in-memory outcomes) are left out.
 */
export function featureLines(data: WalkData): { n: number; text: string }[] {
  const used = new Set(WALK.flatMap((w) => w.marks.map((m) => m.line)));
  // Blocks are separated by blank lines: the feature's header, the Background, then one per scenario (with any tag or
  // comment above it).
  const blocks: { n: number; text: string }[][] = [[]];
  data.feature.text.split("\n").forEach((text, i) => {
    if (text.trim() === "") {
      if (blocks[blocks.length - 1].length) blocks.push([]);
      return;
    }
    blocks[blocks.length - 1].push({ n: i + 1, text });
  });
  const kept = blocks.filter((b, i) => b.length && (i < 2 || b.some((l) => used.has(l.n))));
  return kept.flatMap((b, i) => (i === 0 ? b : [{ n: b[0].n - 1, text: "" }, ...b]));
}

/**
 * The same lines as featureLines, as blocks: the header with the Background first, then each scenario the story acts
 * out, in the order the story first reaches it, so the blocks read left to right as the walk goes.
 */
export function featureBlocks(data: WalkData): { n: number; text: string }[][] {
  const blocks: { n: number; text: string }[][] = [];
  for (const line of featureLines(data)) {
    if (line.text.trim() === "") blocks.push([]);
    else (blocks.at(-1) ?? blocks[blocks.push([]) - 1]).push(line);
  }
  const kept = blocks.filter((b) => b.length);
  // The Feature line and its description are one block and the Background another: show them as one.
  const [title, background, ...scenarios] = kept;
  const first = (b: { n: number }[]) => {
    const i = WALK.findIndex((w) => w.marks.some((m) => b.some((l) => l.n === m.line)));
    return i < 0 ? Number.MAX_SAFE_INTEGER : i;
  };
  return [[...title, ...background], ...scenarios.sort((a, b) => first(a) - first(b))];
}
