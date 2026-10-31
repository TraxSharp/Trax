import type { SnapshotMachine } from './machine';
import { MAX_NESTING, nestsTooDeep } from './nesting';
import { RefusalCode } from './results';
import type { Snapshot } from './types';

/** One event of a plan: a trigger, and its input when it takes one (the fields event's). */
export interface PlannedEvent<T extends string = string> {
  trigger: T;
  input?: unknown;
}

/**
 * Where a plan should take a snapshot. `target` is the state to reach; `fields` the event that writes the page's
 * fields and its input, or null when there are none to write; `navigation` the triggers, sent without input, that move
 * between states, tried in the order given; `maxDepth` the longest plan to look for, twice the machine's states when
 * absent.
 */
export interface PlanRequest<S extends string = string, T extends string = string> {
  target: S;
  fields?: PlannedEvent<T> | null;
  navigation: readonly T[];
  maxDepth?: number;
}

/**
 * What a plan came to. `plan` holds the events to send, none when the snapshot is already there. `unreachable`: no
 * events reach the target. `exceeded`: none within `maxDepth` events, or within the search's limit. `undetermined`:
 * nothing else reaches it, and an edge on a custom rule this machine holds no handler for might; send `events`, then
 * `trigger` without a prediction, and plan again from the answer. `invalid`: the fields input or the snapshot's context
 * nests deeper than 512 arrays and objects (`code`), the target is not a state, or a trigger cannot be planned with
 * (unknown, effect-bound, an outcome, a timer, or the fields trigger in `navigation`).
 */
export type PlanResult<T extends string = string> =
  | { result: 'plan'; events: PlannedEvent<T>[] }
  | { result: 'unreachable' }
  | { result: 'exceeded' }
  | { result: 'undetermined'; trigger: T; events: PlannedEvent<T>[] }
  | { result: 'invalid'; trigger: string }
  | { result: 'invalid'; target: string }
  | { result: 'invalid'; code: typeof RefusalCode.InvalidInput | typeof RefusalCode.InvalidContext };

// A value with every null member left out, at every depth, as a host answers it (the client's own copy is in
// client/machines.ts, which the engine does not import). Past MAX_NESTING it throws rather than recurse further: the
// start was checked, so only a successor a reduction nested deeper gets there, and the search reaches nothing through it.
const withoutNulls = (value: unknown, depth = 0): unknown => {
  if (value === null || typeof value !== 'object') return value;
  if (depth > MAX_NESTING) throw new RangeError('The context nests too deep to compare.');
  if (Array.isArray(value)) return value.map((v) => withoutNulls(v, depth + 1));
  return Object.fromEntries(
    Object.entries(value as Record<string, unknown>)
      .filter(([, v]) => v !== null && v !== undefined)
      .map(([k, v]) => [k, withoutNulls(v, depth + 1)]),
  );
};

// The most snapshots one search expands, whatever its depth.
export const EXPANSION_LIMIT = 10_000;

type Advanced = ReturnType<SnapshotMachine<string, string>['advance']>;

// A snapshot waiting to be expanded. It holds the node it was reached from and the edge that reached it rather than its
// whole path, and, when the goal test already applied the fields event to it, that step and its wire, which the
// expansion reuses instead of advancing it again.
interface Node {
  snapshot: Snapshot;
  parent: number;
  edge: number;
  depth: number;
  applied: { next: Advanced; wire: string | null } | null;
}

/**
 * Breadth first from `snapshot`, over the fields event and then the navigation triggers in the caller's order, each
 * through `advance`, never visiting a canonical wire twice. Every engine searches the same way, so every engine answers
 * the same plan (conformance/PROTOCOL.md, `plan`).
 */
export function searchPlan(
  machine: SnapshotMachine<string, string>,
  snapshot: Snapshot,
  request: PlanRequest,
): PlanResult {
  const def = machine.definition;
  const { target } = request;
  const fields = request.fields ?? null;
  // A trigger listed twice is tried once, where it is first listed.
  const navigation = [...new Set(request.navigation)];

  // Checked first, as advance would refuse them: the search copies and compares both.
  if (fields !== null && nestsTooDeep(fields.input)) return { result: 'invalid', code: RefusalCode.InvalidInput };
  if (nestsTooDeep(snapshot.context)) return { result: 'invalid', code: RefusalCode.InvalidContext };
  if (!def.states.includes(target)) return { result: 'invalid', target };

  const outcomes = new Set<string>(def.outcomeTriggers ?? []);
  const effects = new Set<string>(def.effectTriggers ?? []);
  const timers = new Set<string>(
    Object.values(def.timers ?? {}).flatMap((t) => (t ? [t.trigger] : [])),
  );
  const known = new Set<string>([...def.transitions.map((t) => t.trigger), ...timers]);
  const unusable = (trigger: string) =>
    !known.has(trigger) || effects.has(trigger) || outcomes.has(trigger) || timers.has(trigger);
  if (fields !== null && unusable(fields.trigger)) return { result: 'invalid', trigger: fields.trigger };
  for (const trigger of navigation)
    if (unusable(trigger) || (fields !== null && trigger === fields.trigger)) return { result: 'invalid', trigger };

  const fieldsEvent: PlannedEvent | null =
    fields === null
      ? null
      : fields.input === undefined
        ? { trigger: fields.trigger }
        : { trigger: fields.trigger, input: fields.input };
  const edges: PlannedEvent[] = [
    ...(fieldsEvent ? [fieldsEvent] : []),
    ...navigation.map((trigger) => ({ trigger })),
  ];
  const unpredictableAt = (state: string): readonly string[] => def.unpredictable?.[state] ?? [];

  // Snapshots are compared by their canonical wire without the members that hold null, at every depth: a host leaves
  // those out, so a snapshot it confirmed never carries them, and a reduction that writes one must not read as a change.
  const wireOf = (candidate: Snapshot): string =>
    machine.serialize({ ...candidate, context: withoutNulls(candidate.context) as Record<string, unknown> });

  // The goal: the target state, where the fields event, when there is one, changes nothing. A target that refuses the
  // fields event, or where it rests on a custom rule this machine cannot evaluate, is never the goal. Answers the
  // fields event's step as well when it took one, with its wire when it transitioned.
  let applied: Node['applied'] = null;
  const isGoal = (candidate: Snapshot, wire: string): boolean => {
    applied = null;
    if (candidate.state !== target) return false;
    if (fieldsEvent === null) return true;
    if (unpredictableAt(candidate.state).includes(fieldsEvent.trigger)) return false;
    const next = machine.advance(candidate, fieldsEvent.trigger, fieldsEvent.input);
    applied = { next, wire: next.outcome === 'transitioned' ? wireOf(next.snapshot) : null };
    return applied.wire === wire;
  };

  try {
    const maxDepth = request.maxDepth ?? 2 * def.states.length;
    const startWire = wireOf(snapshot);
    if (isGoal(snapshot, startWire)) return { result: 'plan', events: [] };
    if (maxDepth <= 0) return { result: 'exceeded' };

    const seen = new Set<string>([startWire]);
    const queue: Node[] = [{ snapshot, parent: -1, edge: -1, depth: 0, applied }];
    // The events on the path to the node at `at`, each a copy of its edge.
    const pathTo = (at: number): PlannedEvent[] => {
      const events: PlannedEvent[] = [];
      for (let i = at; queue[i].parent >= 0; i = queue[i].parent) events.push({ ...edges[queue[i].edge] });
      return events.reverse();
    };
    let undetermined: { trigger: string; at: number } | null = null;
    let truncated = false;
    let expanded = 0;

    for (let head = 0; head < queue.length; head++) {
      // Exceeded only while a snapshot still waits: a search that runs out at the limit found everything.
      if (expanded === EXPANSION_LIMIT) {
        truncated = true;
        break;
      }
      expanded++;
      const node = queue[head];
      const unpredictable = unpredictableAt(node.snapshot.state);
      for (let k = 0; k < edges.length; k++) {
        const edge = edges[k];
        // An edge on a custom rule this machine cannot evaluate is the server's to decide: not tried, but the first one
        // met is kept, in case nothing else reaches the target.
        if (unpredictable.includes(edge.trigger)) {
          undetermined ??= { trigger: edge.trigger, at: head };
          continue;
        }
        // The fields event is the first edge, and the goal test may have taken that step already.
        const reused = k === 0 && fieldsEvent !== null ? node.applied : null;
        const next = reused?.next ?? machine.advance(node.snapshot, edge.trigger, edge.input);
        if (next.outcome !== 'transitioned') continue;
        const wire = reused?.wire ?? wireOf(next.snapshot);
        if (seen.has(wire)) continue;
        seen.add(wire);
        if (isGoal(next.snapshot, wire)) return { result: 'plan', events: [...pathTo(head), { ...edge }] };
        if (node.depth + 1 >= maxDepth) {
          truncated = true;
          continue;
        }
        queue.push({ snapshot: next.snapshot, parent: head, edge: k, depth: node.depth + 1, applied });
      }
      // Expanded: what it held is not needed again.
      node.applied = null;
    }

    // A kept custom edge outranks a limit: past it may lie a plan the search could not see.
    if (undetermined !== null)
      return { result: 'undetermined', trigger: undetermined.trigger, events: pathTo(undetermined.at) };
    return truncated ? { result: 'exceeded' } : { result: 'unreachable' };
  } catch {
    // Totality backstop: a snapshot whose context cannot be written as canonical JSON reaches nothing.
    return { result: 'unreachable' };
  }
}
