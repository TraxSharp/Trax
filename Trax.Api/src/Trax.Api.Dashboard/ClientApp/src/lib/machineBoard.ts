// The event-storming board of a machine served from features (operations.machineBoard), laid out the way a team
// would stick it on a wall: one lane per machine, read left to right in the order its states are reached. Each
// column is one command with the actors who send it above, the rules that send or guard it above that, and what
// happens below. A timer or a run's outcome that moves the machine with no command gets a column of its own. The
// machine itself is the lane's first note, with whoever starts it above, and the views that read it end the lane.

export interface BoardScenario {
  file: string;
  line: number;
  scenario: string;
}

export interface BoardSticky {
  id: string;
  kind: string;
  machine: string;
  label: string;
  scenarios: BoardScenario[];
}

export interface Board {
  machine: string;
  lanes: string[];
  stickies: BoardSticky[];
  links: { from: string; to: string }[];
}

/** Whether the host file the board was drawn from had provenance: some sticky names a scenario. */
export function hasProvenance(board: Board): boolean {
  return board.stickies.some((s) => s.scenarios.length > 0);
}

/** One note on the wall. A sticky the board repeats (an actor, a rule) has a note in each column it belongs to. */
export interface BoardNote {
  key: string;
  sticky: BoardSticky;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface BoardArrow {
  key: string;
  from: BoardNote;
  to: BoardNote;
  /** Both ends in one column of one lane: a short arrow down the column, drawn always. */
  local: boolean;
}

export interface BoardLane {
  machine: string;
  y: number;
  h: number;
}

export interface BoardLayout {
  width: number;
  height: number;
  lanes: BoardLane[];
  notes: BoardNote[];
  arrows: BoardArrow[];
}

export const NOTE = 112;
export const ACTOR_H = 40;
const COL = NOTE + 36;
const GAP = 12;
const PAD = 20;

// What a column holds, before it has a place.
interface Column {
  actors: BoardSticky[];
  policies: BoardSticky[];
  // The command, the aggregate or a view: what sits on the column's middle row.
  middle: BoardSticky | null;
  events: BoardSticky[];
}

/**
 * An event's id is event:<machine>:<from>:...:<to>, which is the only place the board says which state it leaves.
 * The label is for people.
 */
function stateOf(event: BoardSticky): { from: string; to: string } {
  const parts = event.id.split(":");
  return { from: parts[2] ?? "", to: parts[parts.length - 1] ?? "" };
}

/**
 * How many steps from the lane's first state each state is, following the lane's events. The first state is the
 * machine's initial one when it is known; else each state no event enters, or, when every state is entered (a
 * machine that comes round again), the first by name.
 */
function depths(events: BoardSticky[], initial: string | undefined): Map<string, number> {
  const next = new Map<string, Set<string>>();
  const entered = new Set<string>();
  for (const e of events) {
    const { from, to } = stateOf(e);
    if (!next.has(from)) next.set(from, new Set());
    next.get(from)!.add(to);
    if (to !== from) entered.add(to);
  }
  const all = [...new Set(events.flatMap((e) => Object.values(stateOf(e))))].sort();
  const unentered = all.filter((s) => !entered.has(s));
  const roots = initial != null && all.includes(initial) ? [initial] : unentered.length > 0 ? unentered : all.slice(0, 1);
  const depth = new Map<string, number>();
  const queue: [string, number][] = roots.map((s) => [s, 0]);
  for (const [s, d] of queue) depth.set(s, d);
  for (let i = 0; i < queue.length; i++) {
    const [s, d] = queue[i];
    for (const t of next.get(s) ?? [])
      if (!depth.has(t)) {
        depth.set(t, d + 1);
        queue.push([t, d + 1]);
      }
  }
  // A state no path reaches still has a place: after everything that is reached.
  let last = Math.max(0, ...depth.values());
  for (const s of all) if (!depth.has(s)) depth.set(s, ++last);
  return depth;
}

/** `initialStates` names each machine's initial state, which a lane starts from when the machine comes round again. */
export function layoutBoard(board: Board, initialStates: Record<string, string> = {}): BoardLayout {
  const byId = new Map(board.stickies.map((s) => [s.id, s]));
  const into = new Map<string, string[]>();
  const outOf = new Map<string, string[]>();
  for (const l of board.links) {
    if (!byId.has(l.from) || !byId.has(l.to)) continue;
    (outOf.get(l.from) ?? outOf.set(l.from, []).get(l.from)!).push(l.to);
    (into.get(l.to) ?? into.set(l.to, []).get(l.to)!).push(l.from);
  }
  const ofKind = (ids: string[] | undefined, kind: string) =>
    (ids ?? []).map((id) => byId.get(id)!).filter((s) => s.kind === kind);

  const laneNames = [...board.lanes];
  for (const s of board.stickies) if (!laneNames.includes(s.machine)) laneNames.push(s.machine);

  const notes: BoardNote[] = [];
  const lanes: BoardLane[] = [];
  let y = 0;
  let width = 0;

  for (const machine of laneNames) {
    const mine = board.stickies.filter((s) => s.machine === machine);
    if (mine.length === 0) continue;
    const events = mine.filter((s) => s.kind === "event");
    const depth = depths(events, initialStates[machine]);
    const eventDepth = (e: BoardSticky) => depth.get(stateOf(e).from) ?? 0;

    const columns: Column[] = [];
    const aggregate = mine.find((s) => s.kind === "aggregate") ?? null;
    if (aggregate)
      columns.push({ actors: ofKind(into.get(aggregate.id), "actor"), policies: [], middle: aggregate, events: [] });

    // The commands, each with what it causes, in the order the lane reaches them.
    const placed = new Set<string>();
    const commands = mine
      .filter((s) => s.kind === "command")
      .map((c) => {
        const caused = ofKind(outOf.get(c.id), "event").sort((a, b) => eventDepth(a) - eventDepth(b) || a.label.localeCompare(b.label));
        caused.forEach((e) => placed.add(e.id));
        return { c, caused, at: caused.length ? eventDepth(caused[0]) : Number.MAX_SAFE_INTEGER };
      });
    // What moves the machine with no command: a timer, a run's outcome.
    const loose = events
      .filter((e) => !placed.has(e.id))
      .map((e) => ({ c: null as BoardSticky | null, caused: [e], at: eventDepth(e) }));
    for (const { c, caused } of [...commands, ...loose].sort(
      (a, b) => a.at - b.at || (a.c?.label ?? a.caused[0].label).localeCompare(b.c?.label ?? b.caused[0].label),
    )) {
      const policies = c
        ? ofKind(into.get(c.id), "policy")
        : [...new Map(caused.flatMap((e) => ofKind(into.get(e.id), "policy")).map((p) => [p.id, p])).values()];
      columns.push({ actors: c ? ofKind(into.get(c.id), "actor") : [], policies, middle: c, events: caused });
    }

    for (const view of mine.filter((s) => s.kind === "view").sort((a, b) => a.label.localeCompare(b.label)))
      columns.push({ actors: ofKind(outOf.get(view.id), "actor"), policies: [], middle: view, events: [] });

    // A rule nothing above placed (one with no command or event of this lane to sit on) ends the lane.
    const shown = new Set(columns.flatMap((c) => [...c.policies, ...(c.middle ? [c.middle] : [])].map((s) => s.id)));
    for (const p of mine.filter((s) => s.kind === "policy" && !shown.has(s.id)))
      columns.push({ actors: [], policies: [p], middle: null, events: [] });

    const actorRows = Math.max(0, ...columns.map((c) => c.actors.length));
    const policyRows = Math.max(0, ...columns.map((c) => c.policies.length));
    const eventRows = Math.max(0, ...columns.map((c) => c.events.length));
    const top = y + PAD;
    const policyTop = top + actorRows * (ACTOR_H + GAP);
    const middleTop = policyTop + policyRows * (NOTE + GAP);
    const eventTop = middleTop + NOTE + GAP;
    const bottom = eventTop + eventRows * (NOTE + GAP) - (eventRows > 0 ? GAP : 0) + PAD;

    columns.forEach((col, i) => {
      const x = PAD + i * COL;
      const add = (sticky: BoardSticky, ny: number, h: number) =>
        notes.push({ key: `${sticky.id}@${machine}:${i}`, sticky, x, y: ny, w: NOTE, h });
      col.actors.forEach((a, r) => add(a, policyTop - (col.actors.length - r) * (ACTOR_H + GAP), ACTOR_H));
      col.policies.forEach((p, r) => add(p, middleTop - (col.policies.length - r) * (NOTE + GAP), NOTE));
      if (col.middle) add(col.middle, middleTop, NOTE);
      col.events.forEach((e, r) => add(e, eventTop + r * (NOTE + GAP), NOTE));
    });
    lanes.push({ machine, y, h: bottom - y });
    width = Math.max(width, PAD * 2 + columns.length * COL - (COL - NOTE));
    y = bottom;
  }

  // The arrows a wall would draw. An event belongs to its lane's machine, and a view reads the machine at the head of
  // its lane, so neither is drawn: the lane says it.
  const notesOf = new Map<string, BoardNote[]>();
  for (const n of notes) (notesOf.get(n.sticky.id) ?? notesOf.set(n.sticky.id, []).get(n.sticky.id)!).push(n);
  const arrows: BoardArrow[] = [];
  for (const l of board.links) {
    const from = byId.get(l.from);
    const to = byId.get(l.to);
    if (!from || !to) continue;
    if (to.kind === "aggregate" && from.kind === "event") continue;
    if (from.kind === "aggregate" && to.kind === "view") continue;
    const sources = notesOf.get(l.from) ?? [];
    const targets = notesOf.get(l.to) ?? [];
    // A repeated sticky is linked from the copy in the same column when there is one.
    const pairs = targets.flatMap((t) => {
      const same = sources.find((s) => columnOf(s) === columnOf(t));
      return same ? [[same, t] as const] : [];
    });
    const drawn = pairs.length > 0 ? pairs : sources[0] && targets[0] ? [[sources[0], targets[0]] as const] : [];
    for (const [a, b] of drawn) arrows.push({ key: `${a.key}->${b.key}`, from: a, to: b, local: columnOf(a) === columnOf(b) });
  }

  return { width, height: y, lanes, notes, arrows };
}

/** The lane and column a note was placed in, from its key. */
const columnOf = (note: BoardNote) => note.key.slice(note.key.lastIndexOf("@") + 1);

/** Every sticky linked to `id` either way, so a selected note can light up what it touches. */
export function neighbours(board: Board, id: string): Set<string> {
  const near = new Set<string>([id]);
  for (const l of board.links) {
    if (l.from === id) near.add(l.to);
    if (l.to === id) near.add(l.from);
  }
  return near;
}
