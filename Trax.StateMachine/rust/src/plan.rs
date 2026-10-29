// The event planner: the events that bring a snapshot to a target state with a page's fields applied, the Rust twin of
// the planner in the TypeScript and C# engines (conformance/PROTOCOL.md, `plan`). Every engine answers the same plan, so
// the search is fixed: breadth first, the fields event before the navigation triggers in the caller's order, each
// snapshot visited once by its canonical wire with null members left out. Pure and bounded: at most `max_depth` events deep and
// `MAX_PLAN_EXPANSIONS` snapshots expanded.

use std::collections::{HashSet, VecDeque};

use crate::machine::{canonical_wire, Machine, Snapshot};
use crate::value::{map_too_deep, Map, Value};

/// The most snapshots one plan expands before it answers [`PlanAnswer::Exceeded`].
pub const MAX_PLAN_EXPANSIONS: usize = 10_000;

/// One event of a plan: a trigger and its input. A navigation trigger carries none.
#[derive(Clone, Debug, PartialEq)]
pub struct PlanEvent {
    /// The trigger to send.
    pub trigger: String,
    /// Its input, or `None` for one that takes none.
    pub input: Option<Value>,
}

impl PlanEvent {
    /// An event of `trigger` with `input`; an input of `null` is no input.
    pub fn new(trigger: impl Into<String>, input: Option<Value>) -> PlanEvent {
        PlanEvent {
            trigger: trigger.into(),
            input: input.filter(|v| !v.is_null()),
        }
    }
}

/// What a plan was refused for before any search.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum PlanInvalid {
    /// The target is not a state of the machine.
    Target(String),
    /// The fields trigger or a navigation trigger is not one a caller may plan with: one no transition fires on (one
    /// the IR declares and no transition uses among them), one bound to the effect, an outcome, a timer, or a
    /// navigation trigger equal to the fields trigger.
    Trigger(String),
    /// The snapshot's context nests deeper than [`MAX_DEPTH`](crate::MAX_DEPTH), which [`Machine::advance`] refuses as
    /// `INVALID_CONTEXT`.
    Context,
    /// The fields input nests deeper than [`MAX_DEPTH`](crate::MAX_DEPTH), which [`Machine::advance`] refuses as
    /// `INVALID_INPUT`.
    Input,
}

/// What [`Machine::plan`] answers.
#[derive(Clone, Debug, PartialEq)]
pub enum PlanAnswer {
    /// The events to send, in order; none when the snapshot is already the goal.
    Plan(Vec<PlanEvent>),
    /// No event the planner may send reaches the target with the fields applied.
    Unreachable,
    /// The search ended without a plan after leaving a snapshot unexpanded at the depth limit, or after expanding
    /// [`MAX_PLAN_EXPANSIONS`] snapshots.
    Exceeded,
    /// No plan was found, and an edge on a custom rule the machine holds no handler for was not tried: `trigger`, from
    /// the snapshot `events` lead to. A client sends `events`, then `trigger` unpredicted, and plans again from the
    /// answer.
    Undetermined {
        /// The trigger to send unpredicted once `events` have been sent.
        trigger: String,
        /// The events to send first.
        events: Vec<PlanEvent>,
    },
    /// The request was refused before any search.
    Invalid(PlanInvalid),
}

// A step and the wire it leads to, or `None` for one the machine refused.
type Step = Option<(Snapshot, String)>;

struct Node {
    snapshot: Snapshot,
    // The node this one was reached from, and the index of the edge that reached it.
    parent: Option<(usize, usize)>,
    depth: usize,
    // The fields event's step from this snapshot, when the goal test already took it: the expansion reuses it.
    applied: Option<Step>,
}

impl Machine {
    /// The events that take `from` to the state `target` with the fields event `fields` applied, moving through the
    /// triggers in `navigation`, which carry no input. `max_depth` is the longest plan looked for, twice the machine's
    /// states when `None`.
    ///
    /// A snapshot is the goal when its state is `target` and, with `fields` given, advancing it by the fields event
    /// transitions to the same wire. Wires are compared with every null member of the context left out, at every depth,
    /// as the host omits them: a snapshot it answers without a member and one holding it as null are the same. From each snapshot the fields event is tried first, then `navigation` in the order
    /// given, each through [`Machine::advance`]; a refused edge is skipped, and a snapshot whose wire was seen is not
    /// visited again. An edge that [rests on a custom rule](Machine::rests_on_unhandled_rule) is not tried. The plan
    /// never depends on the order the IR lists its transitions in. A snapshot or a fields input built in memory that
    /// nests deeper than [`MAX_DEPTH`](crate::MAX_DEPTH) is [`PlanInvalid::Context`] or [`PlanInvalid::Input`], checked
    /// before anything copies or compares it.
    pub fn plan(
        &self,
        from: &Snapshot,
        target: &str,
        fields: Option<&PlanEvent>,
        navigation: &[String],
        max_depth: Option<usize>,
    ) -> PlanAnswer {
        // Checked first, as advance checks them: the search copies and compares both, and would recurse over them.
        if fields.and_then(|f| f.input.as_ref()).is_some_and(Value::is_too_deep) {
            return PlanAnswer::Invalid(PlanInvalid::Input);
        }
        if map_too_deep(&from.context) {
            return PlanAnswer::Invalid(PlanInvalid::Context);
        }
        if !self.state_set.contains(target) {
            return PlanAnswer::Invalid(PlanInvalid::Target(target.to_owned()));
        }
        // A trigger listed twice is tried once, where it is first listed.
        let mut listed = HashSet::new();
        let edges: Vec<PlanEvent> = fields
            .cloned()
            .into_iter()
            .chain(
                navigation
                    .iter()
                    .filter(|t| listed.insert(t.as_str()))
                    .map(|t| PlanEvent::new(t.as_str(), None)),
            )
            .collect();
        let fields_trigger = fields.map(|f| f.trigger.as_str());
        for (i, edge) in edges.iter().enumerate() {
            let navigates = fields.is_none() || i > 0;
            if !self.plannable(&edge.trigger) || (navigates && fields_trigger == Some(edge.trigger.as_str())) {
                return PlanAnswer::Invalid(PlanInvalid::Trigger(edge.trigger.clone()));
            }
        }
        let max_depth = max_depth.unwrap_or(2 * self.states().len());
        let step = |s: &Snapshot, edge: &PlanEvent| -> Step {
            let next = self.advance(s, &edge.trigger, edge.input.as_ref()).ok()?;
            let at = wire(&next);
            Some((next, at))
        };
        // Whether `s`, whose wire is `at`, is the goal, and the fields event's step from it when the test took one.
        let goal = |s: &Snapshot, at: &str| -> (bool, Option<Step>) {
            let Some(f) = fields else {
                return (s.state == target, None);
            };
            // A fields event the engine cannot evaluate here cannot show the fields are applied.
            if s.state != target || self.rests_on_unhandled_rule(&s.state, &f.trigger) {
                return (false, None);
            }
            let applied = step(s, f);
            (applied.as_ref().is_some_and(|(_, w)| w == at), Some(applied))
        };

        let start = wire(from);
        let (reached, applied) = goal(from, &start);
        if reached {
            return PlanAnswer::Plan(Vec::new());
        }
        let mut nodes = vec![Node {
            snapshot: from.clone(),
            parent: None,
            depth: 0,
            applied,
        }];
        let mut seen: HashSet<String> = HashSet::from([start]);
        let mut queue = VecDeque::from([0usize]);
        let mut undetermined: Option<(String, usize)> = None;
        let mut left_unexpanded = false;
        let mut expanded = 0usize;

        while let Some(at) = queue.pop_front() {
            if nodes[at].depth >= max_depth {
                left_unexpanded = true;
                continue;
            }
            if expanded == MAX_PLAN_EXPANSIONS {
                left_unexpanded = true;
                break;
            }
            expanded += 1;
            // Taken once: nothing reads it after this expansion.
            let mut applied = nodes[at].applied.take();
            for (k, edge) in edges.iter().enumerate() {
                if self.rests_on_unhandled_rule(&nodes[at].snapshot.state, &edge.trigger) {
                    undetermined.get_or_insert_with(|| (edge.trigger.clone(), at));
                    continue;
                }
                // The fields event is the first edge, and the goal test may have taken its step already.
                let reused = if k == 0 && fields.is_some() {
                    applied.take()
                } else {
                    None
                };
                let Some((next, next_wire)) = reused.unwrap_or_else(|| step(&nodes[at].snapshot, edge)) else {
                    continue;
                };
                if seen.contains(&next_wire) {
                    continue;
                }
                let (reached, next_applied) = goal(&next, &next_wire);
                seen.insert(next_wire);
                nodes.push(Node {
                    snapshot: next,
                    parent: Some((at, k)),
                    depth: nodes[at].depth + 1,
                    applied: next_applied,
                });
                let index = nodes.len() - 1;
                if reached {
                    return PlanAnswer::Plan(path(&nodes, &edges, index));
                }
                queue.push_back(index);
            }
        }

        match undetermined {
            Some((trigger, at)) => PlanAnswer::Undetermined {
                trigger,
                events: path(&nodes, &edges, at),
            },
            None if left_unexpanded => PlanAnswer::Exceeded,
            None => PlanAnswer::Unreachable,
        }
    }

    // Whether a caller may plan with `trigger`: one a transition fires on, not bound to the effect, not an outcome and
    // not a timer. A trigger the IR declares and no transition uses is not one, as in the other engines.
    fn plannable(&self, trigger: &str) -> bool {
        self.has_transition_on(trigger)
            && !self.effect_triggers().iter().any(|t| t == trigger)
            && !self.outcome_triggers.contains(trigger)
            && !self.timer_triggers.contains(trigger)
    }
}

// The events on the path from the start to `at`.
fn path(nodes: &[Node], edges: &[PlanEvent], mut at: usize) -> Vec<PlanEvent> {
    let mut events = Vec::new();
    while let Some((parent, edge)) = nodes[at].parent {
        events.push(edges[edge].clone());
        at = parent;
    }
    events.reverse();
    events
}

// The canonical wire with every null member of the context left out, at every depth: what the planner compares, since
// the host answers a context without its null members.
fn wire(snapshot: &Snapshot) -> String {
    canonical_wire(&Snapshot {
        machine: snapshot.machine.clone(),
        version: snapshot.version,
        state: snapshot.state.clone(),
        context: without_nulls(&snapshot.context),
    })
}

fn without_nulls(map: &Map) -> Map {
    map.iter()
        .filter(|(_, v)| !v.is_null())
        .map(|(k, v)| (k.clone(), strip(v)))
        .collect()
}

fn strip(value: &Value) -> Value {
    match value {
        Value::Object(map) => Value::Object(without_nulls(map)),
        Value::Array(items) => Value::Array(items.iter().map(strip).collect()),
        other => other.clone(),
    }
}
