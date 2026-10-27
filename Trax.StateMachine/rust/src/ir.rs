// Reads the IR (conformance/ir.schema.json, the document the C# IrExporter writes) into the rules, reductions and
// schemas the interpreter evaluates. It reads what the TypeScript engine's machineFromIr reads and refuses a document
// it could not interpret, such as a rule kind it does not know; it does not check the rest of the schema.

use std::fmt;

use crate::value::{JsString, Map, Value};

/// Why an IR could not be read.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct IrError {
    pub message: String,
}

impl fmt::Display for IrError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for IrError {}

fn fail<T>(message: impl Into<String>) -> Result<T, IrError> {
    Err(IrError {
        message: message.into(),
    })
}

/// The IR format this engine reads, `irFormatVersion` in ts/src/rules/irFormat.ts.
pub const IR_FORMAT_VERSION: i64 = 1;
/// The oldest IR format this engine still reads.
pub const OLDEST_IR_FORMAT_READ: i64 = 1;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Source {
    Context,
    Input,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum FieldType {
    String,
    Number,
    Integer,
    Boolean,
    Array,
    Object,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum CompareOp {
    Gt,
    Gte,
    Lt,
    Lte,
    Eq,
}

#[derive(Clone, Debug)]
pub(crate) enum Test {
    Present,
    Absent,
    OfType(FieldType),
    NonEmpty,
    OneOf(Vec<JsString>),
    Compare(CompareOp, f64),
    Count(CompareOp, f64),
    Length(CompareOp, f64),
    BoolEquals(bool),
    ArrayOf(FieldType),
}

#[derive(Clone, Debug)]
pub(crate) enum Rule {
    Leaf {
        source: Source,
        field: JsString,
        test: Test,
    },
    All(Vec<Rule>),
    Any(Vec<Rule>),
    Custom(String),
}

#[derive(Clone, Debug)]
pub(crate) enum ValueSource {
    Input(JsString),
    Const(Value),
}

#[derive(Clone, Debug)]
pub(crate) enum Reduction {
    Keep,
    Clear,
    Reset,
    Set(Vec<(JsString, ValueSource)>),
    Custom(String),
}

/// What a field shares with a dictionary value or a list element. Each optional part is checked only when present,
/// as in the TypeScript engine, where an empty list still counts as present.
#[derive(Clone, Debug)]
pub(crate) struct ValueSchema {
    pub ty: FieldType,
    pub nullable: bool,
    pub minimum: Option<f64>,
    pub maximum: Option<f64>,
    pub names: Option<Vec<JsString>>,
    pub exact: Option<Vec<JsString>>,
    pub format: Option<String>,
    pub fields: Option<Vec<FieldSchema>>,
    pub values: Option<Box<ValueSchema>>,
    pub keys: Option<Box<ValueSchema>>,
}

#[derive(Clone, Debug)]
pub(crate) struct FieldSchema {
    pub name: JsString,
    pub value: ValueSchema,
    pub constraints: Vec<Rule>,
}

#[derive(Clone, Debug, Default)]
pub(crate) struct Schema {
    pub fields: Vec<FieldSchema>,
}

#[derive(Clone, Debug)]
pub(crate) struct IrTransition {
    pub from: String,
    pub trigger: String,
    pub to: String,
    pub guard: Option<Rule>,
    pub guard_message: Option<String>,
    pub reduce: Option<Reduction>,
    pub effect: bool,
}

#[derive(Clone, Debug)]
pub(crate) struct IrOutcomeEdge {
    pub to: String,
    pub guard: Option<Rule>,
    pub reduce: Option<Reduction>,
}

#[derive(Clone, Debug)]
pub(crate) struct IrOutcome {
    pub name: String,
    pub state: String,
    pub done: bool,
    pub edges: Vec<IrOutcomeEdge>,
    pub output: Option<Schema>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) struct IrTimerField {
    pub name: String,
    pub min: String,
    pub max: String,
}

#[derive(Clone, Debug)]
pub(crate) struct IrTimer {
    pub state: String,
    pub trigger: String,
    pub target: String,
    pub after: Option<String>,
    pub field: Option<IrTimerField>,
}

#[derive(Clone, Debug)]
pub(crate) struct Ir {
    pub id: String,
    pub version: i32,
    pub initial_state: String,
    pub initial_context: Option<Map>,
    pub states: Vec<String>,
    pub triggers: Vec<String>,
    pub committed_states: Vec<String>,
    pub context: Vec<(String, Schema)>,
    pub inputs: Vec<(String, Schema)>,
    pub invariants: Vec<(String, Rule)>,
    pub transitions: Vec<IrTransition>,
    pub outcomes: Vec<IrOutcome>,
    pub timers: Vec<IrTimer>,
}

// ---- reading helpers ----

fn object<'a>(value: &'a Value, what: &str) -> Result<&'a Map, IrError> {
    match value {
        Value::Object(map) => Ok(map),
        _ => fail(format!("{what} must be an object.")),
    }
}

fn member<'a>(map: &'a Map, key: &str) -> Option<&'a Value> {
    map.get(&JsString::from(key)).filter(|v| !v.is_null())
}

fn required<'a>(map: &'a Map, key: &str, what: &str) -> Result<&'a Value, IrError> {
    member(map, key).ok_or_else(|| IrError {
        message: format!("{what} has no '{key}'."),
    })
}

fn text(value: &Value, what: &str) -> Result<String, IrError> {
    match value {
        Value::String(s) => match s.as_str() {
            Some(s) => Ok(s.to_owned()),
            None => fail(format!("{what} holds a lone surrogate.")),
        },
        _ => fail(format!("{what} must be a string.")),
    }
}

fn js_text(value: &Value, what: &str) -> Result<JsString, IrError> {
    match value {
        Value::String(s) => Ok(s.clone()),
        _ => fail(format!("{what} must be a string.")),
    }
}

// Names an engine that keeps a context as a plain object cannot hold as a field: assigning one would reach the
// object's prototype rather than a member. Every engine refuses them, in any segment of a path, so the three agree.
const PROTOTYPE_NAMES: [&str; 3] = ["__proto__", "constructor", "prototype"];

fn field_name(value: &Value, what: &str) -> Result<JsString, IrError> {
    let name = js_text(value, what)?;
    let text = name.to_string_lossy();
    if text.split('.').any(|segment| PROTOTYPE_NAMES.contains(&segment)) {
        return fail(format!("{what} names the field '{text}', which no engine holds."));
    }
    Ok(name)
}

fn required_text(map: &Map, key: &str, what: &str) -> Result<String, IrError> {
    text(required(map, key, what)?, &format!("{what}'s '{key}'"))
}

fn optional_text(map: &Map, key: &str, what: &str) -> Result<Option<String>, IrError> {
    member(map, key)
        .map(|v| text(v, &format!("{what}'s '{key}'")))
        .transpose()
}

fn array<'a>(value: &'a Value, what: &str) -> Result<&'a Vec<Value>, IrError> {
    match value {
        Value::Array(items) => Ok(items),
        _ => fail(format!("{what} must be an array.")),
    }
}

fn names(value: &Value, what: &str) -> Result<Vec<String>, IrError> {
    array(value, what)?
        .iter()
        .map(|v| text(v, &format!("an item of {what}")))
        .collect()
}

fn js_names(value: &Value, what: &str) -> Result<Vec<JsString>, IrError> {
    array(value, what)?
        .iter()
        .map(|v| js_text(v, &format!("an item of {what}")))
        .collect()
}

fn number(value: &Value, what: &str) -> Result<f64, IrError> {
    match value {
        Value::Number(n) => Ok(*n),
        _ => fail(format!("{what} must be a number.")),
    }
}

fn boolean(value: &Value, what: &str) -> Result<bool, IrError> {
    match value {
        Value::Bool(b) => Ok(*b),
        _ => fail(format!("{what} must be a boolean.")),
    }
}

// The members of an object, in key order, each read by `read`. Keys must be well-formed.
fn entries<T>(
    value: Option<&Value>,
    what: &str,
    read: impl Fn(&Value, &str) -> Result<T, IrError>,
) -> Result<Vec<(String, T)>, IrError> {
    let Some(value) = value else {
        return Ok(Vec::new());
    };
    object(value, what)?
        .iter()
        .map(|(k, v)| {
            let key = k.as_str().ok_or_else(|| IrError {
                message: format!("a key of {what} holds a lone surrogate."),
            })?;
            Ok((key.to_owned(), read(v, &format!("{what}.{key}"))?))
        })
        .collect()
}

// ---- the parts ----

fn field_type(value: &Value, what: &str) -> Result<FieldType, IrError> {
    Ok(match text(value, what)?.as_str() {
        "string" => FieldType::String,
        "number" => FieldType::Number,
        "integer" => FieldType::Integer,
        "boolean" => FieldType::Boolean,
        "array" => FieldType::Array,
        "object" => FieldType::Object,
        other => return fail(format!("{what} names an unknown type '{other}'.")),
    })
}

fn compare_op(value: &Value, what: &str) -> Result<CompareOp, IrError> {
    Ok(match text(value, what)?.as_str() {
        "gt" => CompareOp::Gt,
        "gte" => CompareOp::Gte,
        "lt" => CompareOp::Lt,
        "lte" => CompareOp::Lte,
        "eq" => CompareOp::Eq,
        other => return fail(format!("{what} names an unknown operator '{other}'.")),
    })
}

pub(crate) fn rule(value: &Value, what: &str) -> Result<Rule, IrError> {
    let map = object(value, what)?;
    let kind = required_text(map, "rule", what)?;
    let rules = |key: &str| -> Result<Vec<Rule>, IrError> {
        array(required(map, key, what)?, what)?
            .iter()
            .map(|r| rule(r, what))
            .collect()
    };
    match kind.as_str() {
        "all" => return Ok(Rule::All(rules("rules")?)),
        "any" => return Ok(Rule::Any(rules("rules")?)),
        "custom" => return Ok(Rule::Custom(required_text(map, "name", what)?)),
        _ => {}
    }
    let source = match required_text(map, "source", what)?.as_str() {
        "context" => Source::Context,
        "input" => Source::Input,
        other => return fail(format!("{what} reads an unknown source '{other}'.")),
    };
    let field = field_name(required(map, "field", what)?, what)?;
    let op = || compare_op(required(map, "op", what)?, what);
    let target = || number(required(map, "value", what)?, what);
    let test = match kind.as_str() {
        "present" => Test::Present,
        "absent" => Test::Absent,
        "nonEmpty" => Test::NonEmpty,
        "ofType" => Test::OfType(field_type(required(map, "type", what)?, what)?),
        "arrayOf" => Test::ArrayOf(field_type(required(map, "type", what)?, what)?),
        "oneOf" => Test::OneOf(js_names(required(map, "values", what)?, what)?),
        "compare" => Test::Compare(op()?, target()?),
        "count" => Test::Count(op()?, target()?),
        "length" => Test::Length(op()?, target()?),
        "boolEquals" => Test::BoolEquals(boolean(required(map, "value", what)?, what)?),
        other => return fail(format!("{what} is an unknown rule '{other}'.")),
    };
    Ok(Rule::Leaf { source, field, test })
}

fn reduction(value: &Value, what: &str) -> Result<Reduction, IrError> {
    let map = object(value, what)?;
    Ok(match required_text(map, "reduce", what)?.as_str() {
        "keep" => Reduction::Keep,
        "clear" => Reduction::Clear,
        "reset" => Reduction::Reset,
        "custom" => Reduction::Custom(required_text(map, "name", what)?),
        "set" => Reduction::Set(
            array(required(map, "steps", what)?, what)?
                .iter()
                .map(|step| {
                    let step = object(step, what)?;
                    let field = field_name(required(step, "field", what)?, what)?;
                    let source = object(required(step, "value", what)?, what)?;
                    // As resolveSource: an `input` source wins, and a missing constant is null.
                    let value = match source.get(&JsString::from("input")) {
                        Some(input) => ValueSource::Input(field_name(input, what)?),
                        None => {
                            ValueSource::Const(source.get(&JsString::from("const")).cloned().unwrap_or(Value::Null))
                        }
                    };
                    Ok((field, value))
                })
                .collect::<Result<_, IrError>>()?,
        ),
        other => return fail(format!("{what} is an unknown reduction '{other}'.")),
    })
}

fn value_schema(map: &Map, what: &str) -> Result<ValueSchema, IrError> {
    let nested = |key: &str| -> Result<Option<Box<ValueSchema>>, IrError> {
        member(map, key)
            .map(|v| Ok(Box::new(value_schema(object(v, what)?, what)?)))
            .transpose()
    };
    Ok(ValueSchema {
        ty: field_type(required(map, "type", what)?, what)?,
        nullable: member(map, "nullable")
            .map(|v| boolean(v, what))
            .transpose()?
            .unwrap_or(false),
        minimum: member(map, "minimum").map(|v| number(v, what)).transpose()?,
        maximum: member(map, "maximum").map(|v| number(v, what)).transpose()?,
        names: member(map, "enum").map(|v| js_names(v, what)).transpose()?,
        exact: member(map, "enumExact").map(|v| js_names(v, what)).transpose()?,
        format: optional_text(map, "format", what)?,
        fields: member(map, "fields").map(|v| fields(v, what)).transpose()?,
        values: nested("values")?,
        keys: nested("keys")?,
    })
}

fn fields(value: &Value, what: &str) -> Result<Vec<FieldSchema>, IrError> {
    array(value, what)?
        .iter()
        .map(|field| {
            let map = object(field, what)?;
            let name = field_name(required(map, "name", what)?, what)?;
            let what = format!("{what}.{}", name.to_string_lossy());
            Ok(FieldSchema {
                value: value_schema(map, &what)?,
                constraints: member(map, "constraints")
                    .map(|v| {
                        array(v, &what)?
                            .iter()
                            .map(|r| rule(r, &what))
                            .collect::<Result<Vec<_>, _>>()
                    })
                    .transpose()?
                    .unwrap_or_default(),
                name,
            })
        })
        .collect()
}

pub(crate) fn schema(value: &Value, what: &str) -> Result<Schema, IrError> {
    let map = object(value, what)?;
    Ok(Schema {
        fields: fields(required(map, "fields", what)?, what)?,
    })
}

fn transition(value: &Value) -> Result<IrTransition, IrError> {
    let what = "a transition";
    let map = object(value, what)?;
    Ok(IrTransition {
        from: required_text(map, "from", what)?,
        trigger: required_text(map, "trigger", what)?,
        to: required_text(map, "to", what)?,
        guard: member(map, "guard").map(|v| rule(v, what)).transpose()?,
        guard_message: optional_text(map, "guardMessage", what)?,
        reduce: member(map, "reduce").map(|v| reduction(v, what)).transpose()?,
        effect: member(map, "effect").is_some(),
    })
}

fn outcome(name: String, value: &Value) -> Result<IrOutcome, IrError> {
    let what = format!("the outcome {name}");
    let map = object(value, &what)?;
    let done = match required_text(map, "outcome", &what)?.as_str() {
        "done" => true,
        "failed" | "cancelled" => false,
        other => return fail(format!("{what} is an unknown outcome '{other}'.")),
    };
    let edges = array(required(map, "edges", &what)?, &what)?
        .iter()
        .map(|edge| {
            let edge = object(edge, &what)?;
            Ok(IrOutcomeEdge {
                to: required_text(edge, "to", &what)?,
                guard: member(edge, "guard").map(|v| rule(v, &what)).transpose()?,
                reduce: member(edge, "reduce").map(|v| reduction(v, &what)).transpose()?,
            })
        })
        .collect::<Result<_, IrError>>()?;
    Ok(IrOutcome {
        state: required_text(map, "state", &what)?,
        done,
        edges,
        output: member(map, "output").map(|v| schema(v, &what)).transpose()?,
        name,
    })
}

fn timer(value: &Value) -> Result<IrTimer, IrError> {
    let what = "a timer";
    let map = object(value, what)?;
    let field = match member(map, "field") {
        None => None,
        Some(field) => {
            let field = object(field, what)?;
            Some(IrTimerField {
                name: required_text(field, "name", what)?,
                min: required_text(field, "min", what)?,
                max: required_text(field, "max", what)?,
            })
        }
    };
    Ok(IrTimer {
        state: required_text(map, "state", what)?,
        trigger: required_text(map, "trigger", what)?,
        target: required_text(map, "target", what)?,
        after: optional_text(map, "after", what)?,
        field,
    })
}

/// Refuses an IR whose `irFormat` this engine cannot read, as `requireReadableIrFormat` does. An IR without one is not
/// checked.
fn readable_format(value: Option<&Value>) -> Result<(), IrError> {
    let Some(value) = value else { return Ok(()) };
    let format = match value {
        Value::Number(n) if n.is_finite() && n.fract() == 0.0 => *n,
        other => {
            return fail(format!(
                "This state-machine IR names its format as {}, which is not a format number. Regenerate the twin \
                 (trax machine generate).",
                other.to_json()
            ))
        }
    };
    if format > IR_FORMAT_VERSION as f64 {
        return fail(format!(
            "This state-machine IR is format {format}, but this engine reads only up to format {IR_FORMAT_VERSION}."
        ));
    }
    if format < OLDEST_IR_FORMAT_READ as f64 {
        return fail(format!(
            "This state-machine IR is format {format}, which this engine no longer reads (formats \
             {OLDEST_IR_FORMAT_READ} to {IR_FORMAT_VERSION})."
        ));
    }
    Ok(())
}

pub(crate) fn read(value: &Value) -> Result<Ir, IrError> {
    let what = "The IR";
    let map = object(value, what)?;
    readable_format(map.get(&JsString::from("irFormat")))?;

    let version = number(required(map, "version", what)?, "The IR's version")?;
    if version.fract() != 0.0 || version < i32::MIN as f64 || version > i32::MAX as f64 {
        return fail("The IR's version must be an integer.");
    }
    let initial_context = match member(map, "initialContext") {
        None => None,
        Some(v) => Some(object(v, "The IR's initialContext")?.clone()),
    };

    let outcomes = entries(member(map, "outcomes"), "outcomes", |v, _| Ok(v.clone()))?
        .into_iter()
        .map(|(name, v)| outcome(name, &v))
        .collect::<Result<_, IrError>>()?;

    Ok(Ir {
        id: required_text(map, "id", what)?,
        version: version as i32,
        initial_state: required_text(map, "initialState", what)?,
        initial_context,
        states: names(required(map, "states", what)?, "states")?,
        triggers: names(required(map, "triggers", what)?, "triggers")?,
        committed_states: member(map, "committedStates")
            .map(|v| names(v, "committedStates"))
            .transpose()?
            .unwrap_or_default(),
        context: entries(Some(required(map, "context", what)?), "context", schema)?,
        inputs: entries(Some(required(map, "inputs", what)?), "inputs", schema)?,
        invariants: entries(member(map, "invariants"), "invariants", rule)?,
        transitions: array(required(map, "transitions", what)?, "transitions")?
            .iter()
            .map(transition)
            .collect::<Result<_, IrError>>()?,
        outcomes,
        timers: member(map, "timers")
            .map(|v| {
                array(v, "timers")?
                    .iter()
                    .map(timer)
                    .collect::<Result<Vec<_>, IrError>>()
            })
            .transpose()?
            .unwrap_or_default(),
    })
}
