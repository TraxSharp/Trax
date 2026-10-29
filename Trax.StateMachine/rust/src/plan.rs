// The event planner: the events that bring a snapshot to a target state with a page's fields applied, the Rust twin of
// the planner in the TypeScript and C# engines (conformance/PROTOCOL.md, `plan`). Every engine answers the same plan, so
// the search is fixed: breadth first, the fields event before the navigation triggers in the caller's order, each
// snapshot visited once by its canonical wire. Pure and bounded: at most `max_depth` events deep and
// `MAX_PLAN_EXPANSIONS` snapshots expanded.

use std::collections::{HashSet, VecDeque};

use crate::machine::{canonical_wire, Machine, Snapshot};
use crate::value::Value;

/// The most snapshots one plan expands before it answers [`PlanAnswer::Exceeded`].
pub const MAX_PLAN_EXPANSIONS: usize = 10_000;

/// One event of a plan: a trigger and its input. A navigation trigger carries none.
#[derive(Clone, Debug, PartialEq)]
pub struct PlanEvent {
    pub trigger: String,
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
    /// The fields trigger or a navigation trigger is not one a caller may plan with: one the machine does not have,
    /// one bound to the effect, an outcome, a timer, or a navigation trigger equal to the fields trigger.
    Trigger(String),
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
    Undetermined { trigger: String, events: Vec<PlanEvent> },
    /// The request was refused before any search.
    Invalid(PlanInvalid),
}

struct Node {
    snapshot: Snapshot,
    // The node this one was reached from, and the index of the edge that reached it.
    parent: Option<(usize, usize)>,
    depth: usize,
}

impl Machine {
    /// The events that take `from` to the state `target` with the fields event `fields` applied, moving through the
    /// triggers in `navigation`, which carry no input. `max_depth` is the longest plan looked for, twice the machine's
    /// states when `None`.
    ///
    /// A snapshot is the goal when its state is `target` and, with `fields` given, advancing it by the fields event
    /// transitions to the same wire. From each snapshot the fields event is tried first, then `navigation` in the order
    /// given, each through [`Machine::advance`]; a refused edge is skipped, and a snapshot whose wire was seen is not
    /// visited again. An edge that [rests on a custom rule](Machine::rests_on_unhandled_rule) is not tried. The plan
    /// never depends on the order the IR lists its transitions in.
    pub fn plan(
        &self,
        from: &Snapshot,
        target: &str,
        fields: Option<&PlanEvent>,
        navigation: &[String],
        max_depth: Option<usize>,
    ) -> PlanAnswer {
        if !self.state_set.contains(target) {
            return PlanAnswer::Invalid(PlanInvalid::Target(target.to_owned()));
        }
        let edges: Vec<PlanEvent> = fields
            .cloned()
            .into_iter()
            .chain(navigation.iter().map(|t| PlanEvent::new(t.as_str(), None)))
            .collect();
        let fields_trigger = fields.map(|f| f.trigger.as_str());
        for (i, edge) in edges.iter().enumerate() {
            let navigates = fields.is_none() || i > 0;
            if !self.plannable(&edge.trigger) || (navigates && fields_trigger == Some(edge.trigger.as_str())) {
                return PlanAnswer::Invalid(PlanInvalid::Trigger(edge.trigger.clone()));
            }
        }
        let max_depth = max_depth.unwrap_or(2 * self.states().len());
        let is_goal = |s: &Snapshot| {
            s.state == target
                && fields.is_none_or(|f| {
                    // A fields event the engine cannot evaluate here cannot show the fields are applied.
                    !self.rests_on_unhandled_rule(&s.state, &f.trigger)
                        && self
                            .advance(s, &f.trigger, f.input.as_ref())
                            .is_ok_and(|next| canonical_wire(&next) == canonical_wire(s))
                })
        };

        if is_goal(from) {
            return PlanAnswer::Plan(Vec::new());
        }
        let mut nodes = vec![Node {
            snapshot: from.clone(),
            parent: None,
            depth: 0,
        }];
        let mut seen: HashSet<String> = HashSet::from([canonical_wire(from)]);
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
            for (k, edge) in edges.iter().enumerate() {
                let snapshot = &nodes[at].snapshot;
                if self.rests_on_unhandled_rule(&snapshot.state, &edge.trigger) {
                    undetermined.get_or_insert_with(|| (edge.trigger.clone(), at));
                    continue;
                }
                let Ok(next) = self.advance(snapshot, &edge.trigger, edge.input.as_ref()) else {
                    continue;
                };
                if !seen.insert(canonical_wire(&next)) {
                    continue;
                }
                let goal = is_goal(&next);
                nodes.push(Node {
                    snapshot: next,
                    parent: Some((at, k)),
                    depth: nodes[at].depth + 1,
                });
                let reached = nodes.len() - 1;
                if goal {
                    return PlanAnswer::Plan(path(&nodes, &edges, reached));
                }
                queue.push_back(reached);
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

    // Whether a caller may plan with `trigger`: one the machine declares, not bound to the effect, not an outcome and
    // not a timer.
    fn plannable(&self, trigger: &str) -> bool {
        self.triggers.contains(trigger)
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
