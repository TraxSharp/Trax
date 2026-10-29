import type { SnapshotMachine } from './machine';
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
 * `trigger` without a prediction, and plan again from the answer. `invalid`: the target is not a state, or a trigger
 * cannot be planned with (unknown, effect-bound, an outcome, a timer, or the fields trigger in `navigation`).
 */
export type PlanResult<T extends string = string> =
  | { result: 'plan'; events: PlannedEvent<T>[] }
  | { result: 'unreachable' }
  | { result: 'exceeded' }
  | { result: 'undetermined'; trigger: T; events: PlannedEvent<T>[] }
  | { result: 'invalid'; trigger: string }
  | { result: 'invalid'; target: string };

// A value with every null member left out, at every depth, as a host answers it (the client's own copy is in
// client/machines.ts, which the engine does not import).
const withoutNulls = (value: unknown): unknown => {
  if (Array.isArray(value)) return value.map(withoutNulls);
  if (value !== null && typeof value === 'object')
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, v]) => v !== null && v !== undefined)
        .map(([k, v]) => [k, withoutNulls(v)]),
    );
  return value;
};

// The most snapshots one search expands, whatever its depth.
const EXPANSION_LIMIT = 10_000;

interface Node {
  snapshot: Snapshot;
  path: PlannedEvent[];
  depth: number;
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
  const navigation = [...request.navigation];

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
  const copy = (path: PlannedEvent[]) => path.map((e) => ({ ...e }));
  const unpredictableAt = (state: string): readonly string[] => def.unpredictable?.[state] ?? [];

  // Snapshots are compared by their canonical wire without the members that hold null, at every depth: a host leaves
  // those out, so a snapshot it confirmed never carries them, and a reduction that writes one must not read as a change.
  const wireOf = (candidate: Snapshot): string =>
    machine.serialize({ ...candidate, context: withoutNulls(candidate.context) as Record<string, unknown> });

  // The goal: the target state, where the fields event, when there is one, changes nothing. A target that refuses the
  // fields event, or where it rests on a custom rule this machine cannot evaluate, is never the goal.
  const isGoal = (candidate: Snapshot, wire: string): boolean => {
    if (candidate.state !== target) return false;
    if (fieldsEvent === null) return true;
    if (unpredictableAt(candidate.state).includes(fieldsEvent.trigger)) return false;
    const applied = machine.advance(candidate, fieldsEvent.trigger, fieldsEvent.input);
    return applied.outcome === 'transitioned' && wireOf(applied.snapshot) === wire;
  };

  try {
    const maxDepth = request.maxDepth ?? 2 * def.states.length;
    const startWire = wireOf(snapshot);
    if (isGoal(snapshot, startWire)) return { result: 'plan', events: [] };
    if (maxDepth <= 0) return { result: 'exceeded' };

    const seen = new Set<string>([startWire]);
    const queue: Node[] = [{ snapshot, path: [], depth: 0 }];
    let undetermined: { trigger: string; events: PlannedEvent[] } | null = null;
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
      for (const edge of edges) {
        // An edge on a custom rule this machine cannot evaluate is the server's to decide: not tried, but the first one
        // met is kept, in case nothing else reaches the target.
        if (unpredictable.includes(edge.trigger)) {
          undetermined ??= { trigger: edge.trigger, events: copy(node.path) };
          continue;
        }
        const next = machine.advance(node.snapshot, edge.trigger, edge.input);
        if (next.outcome !== 'transitioned') continue;
        const wire = wireOf(next.snapshot);
        if (seen.has(wire)) continue;
        seen.add(wire);
        const path = [...node.path, edge];
        if (isGoal(next.snapshot, wire)) return { result: 'plan', events: copy(path) };
        if (node.depth + 1 >= maxDepth) {
          truncated = true;
          continue;
        }
        queue.push({ snapshot: next.snapshot, path, depth: node.depth + 1 });
      }
    }

    // A kept custom edge outranks a limit: past it may lie a plan the search could not see.
    if (undetermined !== null) return { result: 'undetermined', ...undetermined };
    return truncated ? { result: 'exceeded' } : { result: 'unreachable' };
  } catch {
    // Totality backstop: a snapshot whose context cannot be written as canonical JSON reaches nothing.
    return { result: 'unreachable' };
  }
}
