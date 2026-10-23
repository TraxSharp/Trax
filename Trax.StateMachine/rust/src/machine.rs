// A machine built from its IR, over snapshots: the Rust twin of ts/src/machine.ts and ts/src/rules/irMachine.ts. A
// plain reducer: an entry point matches the edges out of the snapshot's state, takes the first whose guard passes, and
// the destination and the reducer come from that one edge. Every operation is total: a refusal is a value, and a
// custom handler that panics is caught and refused as INTERNAL_ERROR (or MALFORMED, on a read), never unwound into
// the caller.

use std::collections::{BTreeSet, HashMap, HashSet};
use std::panic::{catch_unwind, AssertUnwindSafe};

use crate::formats::duration_ticks;
use crate::ir::{self, FieldType, IrError, Reduction, Rule, Schema};
use crate::refusal::{Refusal, RefusalCode};
use crate::rules::{self, Unhandled};
use crate::stored::{stored_text_problems, unstorable};
use crate::value::{json_copy_map, quote, write_object, JsString, Map, Value};

/// A handler for a custom guard: reads the context and the input, says whether the edge may be taken.
pub type GuardFn = dyn Fn(&Map, Option<&Value>) -> bool + Send + Sync;
/// A handler for a custom reducer: the destination context from the context and the input.
pub type ReducerFn = dyn Fn(&Map, Option<&Value>) -> Map + Send + Sync;
/// A migration from one version to the next: the state and context it reads, the state and context it writes.
pub type MigrationFn = dyn Fn(&str, Map) -> (String, Map) + Send + Sync;

/// The handlers bound to a machine's custom rules, by name. A custom rule the IR names with no handler bound here
/// fails wherever the engine predicts, so it never predicts past it and leaves the step to the server.
#[derive(Default)]
pub(crate) struct CustomRules {
    pub(crate) guards: HashMap<String, Box<GuardFn>>,
    pub(crate) reducers: HashMap<String, Box<ReducerFn>>,
}

/// The language-neutral state of an instance, the only thing that crosses the wire or lands in a row.
#[derive(Clone, Debug, PartialEq)]
pub struct Snapshot {
    pub machine: String,
    pub version: i32,
    pub state: String,
    pub context: Map,
}

impl Snapshot {
    /// Reads a snapshot object (`machine`, `version`, `state`, `context`) without checking it against a machine.
    /// `None` when a member is missing or of the wrong type, or the version is not an integer in the int32 range.
    pub fn from_value(value: &Value) -> Option<Snapshot> {
        Some(Snapshot {
            machine: value.get("machine")?.as_str()?.to_owned(),
            version: as_int(value.get("version")?)?,
            state: value.get("state")?.as_str()?.to_owned(),
            context: value.get("context")?.as_object()?.clone(),
        })
    }

    /// The snapshot as an object.
    pub fn to_value(&self) -> Value {
        let mut map = Map::new();
        map.insert("machine".into(), Value::from(self.machine.as_str()));
        map.insert("version".into(), Value::Number(f64::from(self.version)));
        map.insert("state".into(), Value::from(self.state.as_str()));
        map.insert("context".into(), Value::Object(self.context.clone()));
        Value::Object(map)
    }
}

/// The canonical wire of a snapshot (RFC 8785): the envelope in fixed order, the context's members sorted by UTF-16
/// code unit at every depth, numbers as ECMAScript writes them.
pub fn canonical_wire(snapshot: &Snapshot) -> String {
    let mut out = format!(
        "{{\"machine\":{},\"version\":{},\"state\":{},\"context\":",
        quote(&JsString::from(snapshot.machine.as_str())),
        snapshot.version,
        quote(&JsString::from(snapshot.state.as_str())),
    );
    write_object(&snapshot.context, &mut out);
    out.push('}');
    out
}

/// A state's timer as the machine declares it: on `trigger` the state moves to `target` once the duration has passed.
/// The duration is fixed (`after`) or read from a context field at entry, within bounds (`field`). The engine holds
/// no entry time, so it reports no due time.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Timer {
    pub trigger: String,
    pub target: String,
    pub after: Option<String>,
    pub field: Option<TimerField>,
}

/// The context field a timer reads its duration from, and the ISO-8601 bounds the duration must be within.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TimerField {
    pub name: String,
    pub min: String,
    pub max: String,
}

struct Edge {
    from: String,
    trigger: String,
    to: String,
    guard: Option<Rule>,
    guard_message: Option<String>,
    reduce: Option<Reduction>,
}

struct ContextValidator {
    schema: Option<Schema>,
    invariant: Option<Rule>,
}

/// A machine interpreted from its IR.
///
/// One entry point per trigger kind, as on the server: [`Machine::advance`] takes the triggers a caller sends,
/// [`Machine::apply_outcome`] an invoked train's outcome, and [`Machine::apply_timer`] a state's timer.
pub struct Machine {
    id: String,
    version: i32,
    initial_state: String,
    initial_context: Map,
    states: Vec<String>,
    state_set: HashSet<String>,
    edges: Vec<Edge>,
    outcome_triggers: BTreeSet<String>,
    timer_triggers: HashSet<String>,
    timers: HashMap<String, Timer>,
    input_validators: HashMap<String, Schema>,
    context_validators: HashMap<String, ContextValidator>,
    committed_states: Vec<String>,
    effect_triggers: Vec<String>,
    custom: CustomRules,
    migrations: HashMap<i32, Box<MigrationFn>>,
}

impl std::fmt::Debug for Machine {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Machine")
            .field("id", &self.id)
            .field("version", &self.version)
            .finish_non_exhaustive()
    }
}

// Runs `f`, turning a panic (only a custom handler can raise one) into `None`.
fn shielded<T>(f: impl FnOnce() -> T) -> Option<T> {
    catch_unwind(AssertUnwindSafe(f)).ok()
}

fn refuse<T>(code: RefusalCode, detail: impl Into<String>) -> Result<T, Refusal> {
    Err(Refusal::new(code, detail))
}

// An integral JSON number in the int32 range, as both other engines read a snapshot's version.
fn as_int(value: &Value) -> Option<i32> {
    match value {
        Value::Number(n) if n.is_finite() && n.fract() == 0.0 && *n >= i32::MIN as f64 && *n <= i32::MAX as f64 => {
            Some(*n as i32)
        }
        _ => None,
    }
}

// What an effect-bound trigger that declares no record takes: the receipt the effect runner hands it, or nothing.
fn receipt_input() -> Schema {
    Schema {
        fields: vec![ir::FieldSchema {
            name: "receipt".into(),
            value: ir::ValueSchema {
                ty: FieldType::String,
                nullable: true,
                minimum: None,
                maximum: None,
                names: None,
                exact: None,
                format: None,
                fields: None,
                values: None,
                keys: None,
            },
            constraints: Vec::new(),
        }],
    }
}

// The initial context derived from the initial state's schema, as the C# side derives it: array [], number 0,
// string "" (or an enum's first name), boolean false, object {}, nullable null.
fn defaults_for(schema: &Schema) -> Map {
    let mut ctx = Map::new();
    for field in &schema.fields {
        let value = if field.value.nullable {
            Value::Null
        } else {
            match field.value.ty {
                FieldType::Array => Value::Array(Vec::new()),
                FieldType::Number | FieldType::Integer => Value::Number(0.0),
                FieldType::Boolean => Value::Bool(false),
                FieldType::String => Value::String(
                    field
                        .value
                        .names
                        .as_ref()
                        .and_then(|names| names.first().cloned())
                        .unwrap_or_else(|| JsString::from("")),
                ),
                FieldType::Object => Value::Object(Map::new()),
            }
        };
        ctx.insert(field.name.clone(), value);
    }
    ctx
}

impl Machine {
    /// Builds a machine from its IR document. Refuses an IR it cannot interpret: a format newer than it reads, a
    /// missing section, a rule or reduction it does not know.
    pub fn from_ir(ir: &Value) -> Result<Machine, IrError> {
        let ir = ir::read(ir)?;

        // The machine's actual initial context if the IR carries it, else derived from the initial state's schema.
        let initial_context = match &ir.initial_context {
            Some(ctx) => ctx.clone(),
            None => {
                let schema = ir.context.iter().find(|(state, _)| *state == ir.initial_state);
                schema.map(|(_, s)| defaults_for(s)).unwrap_or_default()
            }
        };

        let mut edges: Vec<Edge> = ir
            .transitions
            .iter()
            .map(|t| Edge {
                from: t.from.clone(),
                trigger: t.trigger.clone(),
                to: t.to.clone(),
                guard: t.guard.clone(),
                guard_message: t.guard_message.clone(),
                reduce: t.reduce.clone(),
            })
            .collect();

        // An outcome is applied through its own edges in declaration order. Its name has a dot, which no C# trigger
        // can, so it never collides with one.
        let mut outcomes: Vec<&ir::IrOutcome> = ir.outcomes.iter().collect();
        outcomes.sort_by(|a, b| a.name.encode_utf16().cmp(b.name.encode_utf16()));
        for outcome in &outcomes {
            for e in &outcome.edges {
                edges.push(Edge {
                    from: outcome.state.clone(),
                    trigger: outcome.name.clone(),
                    to: e.to.clone(),
                    guard: e.guard.clone(),
                    guard_message: None,
                    reduce: e.reduce.clone(),
                });
            }
        }

        // Every trigger's input is checked against one schema before any guard reads it: the record it declares,
        // else nothing, except that an effect-bound trigger takes the receipt. A success outcome's input is the
        // train's output; a failure or a cancel carries none.
        let effect_bound: BTreeSet<String> = ir
            .transitions
            .iter()
            .filter(|t| t.effect)
            .map(|t| t.trigger.clone())
            .collect();
        let mut input_validators = HashMap::new();
        for trigger in &ir.triggers {
            let declared = ir.inputs.iter().find(|(name, _)| name == trigger);
            let schema = match declared {
                Some((_, schema)) => schema.clone(),
                None if effect_bound.contains(trigger) => receipt_input(),
                None => Schema::default(),
            };
            input_validators.insert(trigger.clone(), schema);
        }
        for outcome in &outcomes {
            let schema = if outcome.done {
                outcome.output.clone().unwrap_or_default()
            } else {
                Schema::default()
            };
            input_validators.insert(outcome.name.clone(), schema);
        }

        let mut context_validators: HashMap<String, ContextValidator> = HashMap::new();
        for (state, schema) in &ir.context {
            context_validators
                .entry(state.clone())
                .or_insert(ContextValidator {
                    schema: None,
                    invariant: None,
                })
                .schema = Some(schema.clone());
        }
        for (state, invariant) in &ir.invariants {
            context_validators
                .entry(state.clone())
                .or_insert(ContextValidator {
                    schema: None,
                    invariant: None,
                })
                .invariant = Some(invariant.clone());
        }

        // A timer is a trigger kind of its own: not an edge a caller fires, so it stays out of the edges and only
        // apply_timer takes it. A later timer of the same state replaces an earlier one.
        let mut timers = HashMap::new();
        for t in &ir.timers {
            timers.insert(
                t.state.clone(),
                Timer {
                    trigger: t.trigger.clone(),
                    target: t.target.clone(),
                    after: t.after.clone(),
                    field: t.field.as_ref().map(|f| TimerField {
                        name: f.name.clone(),
                        min: f.min.clone(),
                        max: f.max.clone(),
                    }),
                },
            );
        }
        let timer_triggers = timers.values().map(|t: &Timer| t.trigger.clone()).collect();

        Ok(Machine {
            id: ir.id,
            version: ir.version,
            initial_state: ir.initial_state,
            initial_context,
            state_set: ir.states.iter().cloned().collect(),
            states: ir.states,
            edges,
            outcome_triggers: outcomes.iter().map(|o| o.name.clone()).collect(),
            timer_triggers,
            timers,
            input_validators,
            context_validators,
            committed_states: ir.committed_states,
            effect_triggers: effect_bound.into_iter().collect(),
            custom: CustomRules::default(),
            migrations: HashMap::new(),
        })
    }

    /// Builds a machine from the IR's JSON text.
    pub fn from_ir_text(text: &str) -> Result<Machine, IrError> {
        let value = Value::parse(text).map_err(|e| IrError {
            message: format!("The IR is not JSON: {e}."),
        })?;
        Machine::from_ir(&value)
    }

    /// Binds a handler to the custom guard `name`.
    pub fn with_guard(
        mut self,
        name: impl Into<String>,
        guard: impl Fn(&Map, Option<&Value>) -> bool + Send + Sync + 'static,
    ) -> Machine {
        self.custom.guards.insert(name.into(), Box::new(guard));
        self
    }

    /// Binds a handler to the custom reducer `name`.
    pub fn with_reducer(
        mut self,
        name: impl Into<String>,
        reducer: impl Fn(&Map, Option<&Value>) -> Map + Send + Sync + 'static,
    ) -> Machine {
        self.custom.reducers.insert(name.into(), Box::new(reducer));
        self
    }

    /// Adds the migration from `from_version` to the next version, which [`Machine::rehydrate`] runs on an older
    /// snapshot. The IR carries none: a migration is code, written beside the machine in each language.
    pub fn with_migration(
        mut self,
        from_version: i32,
        migrate: impl Fn(&str, Map) -> (String, Map) + Send + Sync + 'static,
    ) -> Machine {
        self.migrations.insert(from_version, Box::new(migrate));
        self
    }

    pub fn id(&self) -> &str {
        &self.id
    }

    pub fn version(&self) -> i32 {
        self.version
    }

    pub fn initial_state(&self) -> &str {
        &self.initial_state
    }

    pub fn states(&self) -> &[String] {
        &self.states
    }

    /// The states that are committed: once a draft reaches one, an irreversible effect has run.
    pub fn committed_states(&self) -> &[String] {
        &self.committed_states
    }

    /// The triggers whose edge runs an effect. The server's effect runner hands such a trigger its input.
    pub fn effect_triggers(&self) -> &[String] {
        &self.effect_triggers
    }

    /// The outcome triggers of states that invoke a train (`Fetching.done`), which only
    /// [`Machine::apply_outcome`] applies.
    pub fn outcome_triggers(&self) -> impl Iterator<Item = &str> {
        self.outcome_triggers.iter().map(String::as_str)
    }

    /// A new instance: the initial state and the initial context.
    pub fn initial_snapshot(&self) -> Snapshot {
        Snapshot {
            machine: self.id.clone(),
            version: self.version,
            state: self.initial_state.clone(),
            context: self.initial_context.clone(),
        }
    }

    /// Applies a trigger a caller sends. Refusals, first match wins: an unknown state (`NO_TRANSITION`); an outcome
    /// trigger (`OUTCOME_BOUND`); a timer trigger (`TIMER_BOUND`); no edge from the state (`NO_TRANSITION`); an input
    /// that does not match the trigger's schema (`INVALID_INPUT`); a guard that declines (`GUARD_FAILED`); a target
    /// whose timer reads a duration the successor context does not hold within bounds (`INVALID_INPUT`); a successor
    /// context the target refuses (`INVALID_CONTEXT`). An input of `null` is no input.
    pub fn advance(&self, snapshot: &Snapshot, trigger: &str, input: Option<&Value>) -> Result<Snapshot, Refusal> {
        let input = input.filter(|v| !v.is_null());
        if !self.state_set.contains(&snapshot.state) {
            return refuse(
                RefusalCode::NoTransition,
                format!("Unknown state '{}'.", snapshot.state),
            );
        }
        if self.outcome_triggers.contains(trigger) {
            return refuse(
                RefusalCode::OutcomeBound,
                "This step is completed by the outcome of its work, not by an action.",
            );
        }
        if self.timer_triggers.contains(trigger) {
            return refuse(
                RefusalCode::TimerBound,
                "This step moves on by itself when its time is up, not by an action.",
            );
        }
        let matches = self.matches(&snapshot.state, trigger);
        if matches.is_empty() {
            return refuse(
                RefusalCode::NoTransition,
                format!("No transition from '{}' on '{trigger}'.", snapshot.state),
            );
        }
        if let Some(problem) = self.input_problem(trigger, input) {
            return refuse(RefusalCode::InvalidInput, problem);
        }
        shielded(
            || match matches.iter().find(|e| self.guard_passes(e, &snapshot.context, input)) {
                Some(chosen) => self.take(&snapshot.context, chosen, input),
                None => {
                    let detail = matches.iter().find_map(|e| e.guard_message.clone()).unwrap_or_else(|| {
                        format!(
                            "A transition from '{}' on '{trigger}' exists but its guard rejected the input.",
                            snapshot.state
                        )
                    });
                    refuse(RefusalCode::GuardFailed, detail)
                }
            },
        )
        .unwrap_or_else(|| refuse(RefusalCode::InternalError, "A custom handler panicked."))
    }

    /// Applies an invoked train's outcome (`Fetching.done`, `.failed`, `.cancelled`) with the train's output as its
    /// input. A success tries the state's edges in declaration order and takes the first whose guard accepts the
    /// output; an output no edge accepts, a trigger that is not an outcome, and an outcome of a state the snapshot is
    /// not in are `NO_TRANSITION`. An output that does not match the train's output schema is `INVALID_INPUT`.
    pub fn apply_outcome(
        &self,
        snapshot: &Snapshot,
        trigger: &str,
        output: Option<&Value>,
    ) -> Result<Snapshot, Refusal> {
        let output = output.filter(|v| !v.is_null());
        if !self.state_set.contains(&snapshot.state) {
            return refuse(
                RefusalCode::NoTransition,
                format!("Unknown state '{}'.", snapshot.state),
            );
        }
        if !self.outcome_triggers.contains(trigger) {
            return refuse(
                RefusalCode::NoTransition,
                format!("'{trigger}' is not an outcome of this machine."),
            );
        }
        let matches = self.matches(&snapshot.state, trigger);
        if matches.is_empty() {
            return refuse(
                RefusalCode::NoTransition,
                format!("No transition from '{}' on '{trigger}'.", snapshot.state),
            );
        }
        if let Some(problem) = self.input_problem(trigger, output) {
            return refuse(RefusalCode::InvalidInput, problem);
        }
        shielded(|| {
            match matches.iter().find(|e| self.guard_passes(e, &snapshot.context, output)) {
                Some(chosen) => self.take(&snapshot.context, chosen, output),
                // Not a guard a caller could satisfy by sending something else: the output has nowhere to go.
                None => refuse(
                    RefusalCode::NoTransition,
                    format!("No OnDone edge from '{}' accepts the train's output.", snapshot.state),
                ),
            }
        })
        .unwrap_or_else(|| refuse(RefusalCode::InternalError, "A custom handler panicked."))
    }

    /// Fires the timer `trigger` (`<State>.after`) of the snapshot's state, carrying the context to the timer's
    /// target unchanged. A trigger that is not the timer of the snapshot's state is `NO_TRANSITION`; a target whose
    /// own timer reads a duration the context does not hold within bounds is `INVALID_INPUT`; a target that refuses
    /// the context is `INVALID_CONTEXT`.
    pub fn apply_timer(&self, snapshot: &Snapshot, trigger: &str) -> Result<Snapshot, Refusal> {
        if !self.state_set.contains(&snapshot.state) {
            return refuse(
                RefusalCode::NoTransition,
                format!("Unknown state '{}'.", snapshot.state),
            );
        }
        let Some(timer) = self.timers.get(&snapshot.state).filter(|t| t.trigger == trigger) else {
            return refuse(
                RefusalCode::NoTransition,
                format!("No timer fires from '{}' on '{trigger}'.", snapshot.state),
            );
        };
        shielded(|| self.enter(&timer.target, json_copy_map(&snapshot.context)))
            .unwrap_or_else(|| refuse(RefusalCode::InternalError, "A custom handler panicked."))
    }

    /// The timer declared on the snapshot's state, or `None` when it has none.
    pub fn timer(&self, snapshot: &Snapshot) -> Option<Timer> {
        self.timers.get(&snapshot.state).cloned()
    }

    /// Reads stored text as a snapshot of this machine, migrating an older version forward. Refusals: not JSON, not
    /// an object, or a missing or mistyped member (`MALFORMED`); another machine (`UNKNOWN_MACHINE`); a newer version,
    /// or no migration from an older one (`VERSION_MISMATCH`); an unknown state (`UNKNOWN_STATE`); a context the state
    /// refuses (`INVALID_CONTEXT`). A custom rule with no handler counts as failed.
    pub fn rehydrate(&self, text: &str) -> Result<Snapshot, Refusal> {
        self.rehydrate_with(text, false)
    }

    /// Reads a snapshot the server sent: as [`Machine::rehydrate`], except that a custom rule in a state's invariant
    /// with no handler here counts as passed, since the server already evaluated it. The shape and every built-in
    /// rule are still checked.
    pub fn rehydrate_from_server(&self, text: &str) -> Result<Snapshot, Refusal> {
        self.rehydrate_with(text, true)
    }

    fn rehydrate_with(&self, text: &str, from_server: bool) -> Result<Snapshot, Refusal> {
        shielded(|| self.rehydrate_core(text, from_server))
            .unwrap_or_else(|| refuse(RefusalCode::Malformed, "A custom handler panicked."))
    }

    fn rehydrate_core(&self, text: &str, from_server: bool) -> Result<Snapshot, Refusal> {
        let Ok(node) = Value::parse(text) else {
            return refuse(RefusalCode::Malformed, "Input is not valid JSON.");
        };
        // What the C# reader refuses although the parser reads it (stored.rs): too deep, a key twice, a lone surrogate
        // in what it reads first.
        let unreadable = stored_text_problems(text);
        if unreadable.root || unreadable.members.contains("machine") || unreadable.members.contains("state") {
            return refuse(RefusalCode::Malformed, "The snapshot could not be read.");
        }
        let Value::Object(obj) = &node else {
            return refuse(RefusalCode::Malformed, "Snapshot must be a JSON object.");
        };
        let member = |key: &str| obj.get(&JsString::from(key));
        let string = |key: &str| match member(key) {
            Some(Value::String(s)) => Some(s),
            _ => None,
        };
        let machine = string("machine");
        let state = string("state");
        let version = member("version").and_then(as_int);
        let context = member("context").and_then(Value::as_object);
        let (Some(machine), Some(state), Some(version), Some(context)) = (machine, state, version, context) else {
            return refuse(
                RefusalCode::Malformed,
                "Snapshot is missing required fields (machine, version, state, context).",
            );
        };
        let Some(machine) = machine.as_str().filter(|m| *m == self.id) else {
            return refuse(
                RefusalCode::UnknownMachine,
                format!(
                    "Snapshot machine '{}' does not match '{}'.",
                    machine.to_string_lossy(),
                    self.id
                ),
            );
        };
        if version > self.version {
            return refuse(
                RefusalCode::VersionMismatch,
                format!(
                    "Snapshot version {version} is newer than definition version {}.",
                    self.version
                ),
            );
        }
        let Some(state) = state.as_str() else {
            return refuse(
                RefusalCode::UnknownState,
                format!("Unknown state '{}'.", state.to_string_lossy()),
            );
        };
        let mut state = state.to_owned();
        // Read before the copy, which turns a number outside the range of a double into null.
        let stored_problem = (version == self.version).then(|| unstorable(context)).flatten();
        let mut context = json_copy_map(context);
        let migrated = version != self.version;
        let mut version = version;
        while version < self.version {
            let Some(migrate) = self.migrations.get(&version) else {
                return refuse(
                    RefusalCode::VersionMismatch,
                    format!("No migration from version {version} to {}.", self.version),
                );
            };
            (state, context) = migrate(&state, context);
            version += 1;
        }
        if !self.state_set.contains(&state) {
            return refuse(RefusalCode::UnknownState, format!("Unknown state '{state}'."));
        }
        // A context the C# reader cannot read, or holding a value no store can hold, is malformed, as there.
        if unreadable.members.contains("context") {
            return refuse(RefusalCode::Malformed, "The snapshot could not be read.");
        }
        let stored_problem = if migrated { unstorable(&context) } else { stored_problem };
        if let Some(problem) = stored_problem {
            return refuse(RefusalCode::Malformed, problem);
        }
        if let Some(problem) = self.context_problem(&state, &context, from_server) {
            return refuse(RefusalCode::InvalidContext, problem);
        }
        Ok(Snapshot {
            machine: machine.to_owned(),
            version: self.version,
            state,
            context,
        })
    }

    /// The snapshot as canonical wire, without checking it against the machine.
    pub fn serialize(&self, snapshot: &Snapshot) -> String {
        canonical_wire(snapshot)
    }

    /// Whether [`Machine::advance`] would take `trigger` with `input`: false for an outcome or a timer, an input that
    /// does not match, or no edge whose guard passes.
    pub fn can_fire(&self, snapshot: &Snapshot, trigger: &str, input: Option<&Value>) -> bool {
        let input = input.filter(|v| !v.is_null());
        if !self.state_set.contains(&snapshot.state)
            || self.outcome_triggers.contains(trigger)
            || self.timer_triggers.contains(trigger)
            || self.input_problem(trigger, input).is_some()
        {
            return false;
        }
        shielded(|| {
            self.matches(&snapshot.state, trigger)
                .iter()
                .any(|e| self.guard_passes(e, &snapshot.context, input))
        })
        .unwrap_or(false)
    }

    /// The triggers with an edge out of the snapshot's state, in declaration order, without the outcome triggers.
    /// Guards are not evaluated.
    pub fn available_triggers(&self, snapshot: &Snapshot) -> Vec<String> {
        if !self.state_set.contains(&snapshot.state) {
            return Vec::new();
        }
        let mut seen: Vec<String> = Vec::new();
        for e in &self.edges {
            if e.from == snapshot.state && !self.outcome_triggers.contains(&e.trigger) && !seen.contains(&e.trigger) {
                seen.push(e.trigger.clone());
            }
        }
        seen
    }

    fn matches(&self, state: &str, trigger: &str) -> Vec<&Edge> {
        self.edges
            .iter()
            .filter(|e| e.from == state && e.trigger == trigger)
            .collect()
    }

    fn guard_passes(&self, edge: &Edge, context: &Map, input: Option<&Value>) -> bool {
        edge.guard
            .as_ref()
            .is_none_or(|g| rules::evaluate(g, context, input, &self.custom, Unhandled::Fails))
    }

    fn input_problem(&self, trigger: &str, input: Option<&Value>) -> Option<String> {
        self.input_validators
            .get(trigger)
            .and_then(|schema| rules::validate_input(schema, input))
    }

    // The chosen edge is the single source of both the destination and the reducer. With no reducer the context is
    // carried as a JSON copy, as the TypeScript engine carries it.
    fn take(&self, context: &Map, chosen: &Edge, input: Option<&Value>) -> Result<Snapshot, Refusal> {
        let next = match &chosen.reduce {
            Some(reduction) => rules::reduce(reduction, context, input, &self.initial_context, &self.custom),
            None => json_copy_map(context),
        };
        self.enter(&chosen.to, next)
    }

    // The successor of a transition into `state`: the target's timer reads its duration from the context first, so a
    // duration out of bounds is refused as input before the context is validated, as on the server.
    fn enter(&self, state: &str, context: Map) -> Result<Snapshot, Refusal> {
        if let Some(problem) = self.timer_problem(state, &context) {
            return refuse(RefusalCode::InvalidInput, problem);
        }
        if let Some(problem) = self.context_problem(state, &context, false) {
            return refuse(RefusalCode::InvalidContext, problem);
        }
        Ok(Snapshot {
            machine: self.id.clone(),
            version: self.version,
            state: state.to_owned(),
            context,
        })
    }

    // Why entering `state` with `context` cannot arm its timer, or `None` when the state has no timer, a fixed one, or
    // a duration in bounds.
    fn timer_problem(&self, state: &str, context: &Map) -> Option<String> {
        let field = self.timers.get(state)?.field.as_ref()?;
        let held = context.get(&JsString::from(field.name.as_str()));
        let Some(ticks) = held.and_then(Value::as_str).and_then(duration_ticks) else {
            return Some(format!(
                "The timer of {state} reads '{}', which must hold an ISO-8601 duration such as \"PT5M\".",
                field.name
            ));
        };
        match (duration_ticks(&field.min), duration_ticks(&field.max)) {
            (Some(min), Some(max)) if ticks >= min && ticks <= max => None,
            _ => Some(format!(
                "The timer of {state} waits {}, outside its bounds of {} to {}.",
                held.and_then(Value::as_str).unwrap_or_default(),
                field.min,
                field.max
            )),
        }
    }

    fn context_problem(&self, state: &str, context: &Map, from_server: bool) -> Option<String> {
        let validator = self.context_validators.get(state)?;
        if let Some(schema) = &validator.schema {
            if let Some(problem) = rules::validate_schema(schema, context) {
                return Some(problem);
            }
        }
        // The state's requirement, after the shape. On a snapshot the server sent, a custom rule with no handler here
        // counts as passed: the server evaluated it.
        let unhandled = if from_server {
            Unhandled::Passes
        } else {
            Unhandled::Fails
        };
        match &validator.invariant {
            Some(invariant) if !rules::evaluate(invariant, context, None, &self.custom, unhandled) => {
                Some("A state requirement was not satisfied.".to_owned())
            }
            _ => None,
        }
    }
}
