// The Rust engine as a conformance testee (conformance/PROTOCOL.md): one JSON request per line on stdin, one answer
// per line on stdout. It builds each machine from the IR it is loaded with and answers through the crate's public API
// only, so a bug in how a value crosses that API is caught too.

use std::collections::HashMap;
use std::io::{self, BufRead, Write};

use trax_state_machine::{
    canonical_wire, JsString, Machine, Map, PlanAnswer, PlanEvent, PlanInvalid, Refusal, Snapshot, Value,
};

fn main() {
    // The kit's `throws` handler panics on purpose; the engine catches it, so the panic message is noise.
    std::panic::set_hook(Box::new(|_| {}));
    let stdin = io::stdin();
    let mut stdout = io::stdout().lock();
    let mut machines: HashMap<String, Machine> = HashMap::new();
    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        if line.trim().is_empty() {
            continue;
        }
        let (answer, end) = match Value::parse(&line) {
            Ok(request) => {
                let end = request.get("op").and_then(Value::as_str) == Some("end");
                (handle(&request, &mut machines), end)
            }
            Err(e) => (
                answer_with(None, vec![("error", Value::from(format!("not JSON: {e}")))]),
                false,
            ),
        };
        if writeln!(stdout, "{}", answer.to_json())
            .and_then(|_| stdout.flush())
            .is_err()
        {
            break;
        }
        if end {
            break;
        }
    }
}

fn answer_with(id: Option<&Value>, members: Vec<(&str, Value)>) -> Value {
    let mut map = Map::new();
    if let Some(id) = id {
        map.insert("id".into(), id.clone());
    }
    for (key, value) in members {
        map.insert(key.into(), value);
    }
    Value::Object(map)
}

fn handle(request: &Value, machines: &mut HashMap<String, Machine>) -> Value {
    let id = request.get("id");
    let answer = |members: Vec<(&str, Value)>| answer_with(id, members);
    let error = |message: String| answer_with(id, vec![("error", Value::from(message))]);
    let op = request.get("op").and_then(Value::as_str).unwrap_or_default();
    let name = request.get("machine").and_then(Value::as_str);

    match op {
        "load" => {
            let (Some(name), Some(ir)) = (name, request.get("ir")) else {
                return error("a load names a machine and carries its ir".into());
            };
            match Machine::from_ir(ir) {
                Ok(machine) => {
                    // The one handler every testee binds (PROTOCOL.md): a custom guard named `throws`, which panics.
                    let machine = machine.with_guard("throws", |_, _| panic!("throws"));
                    machines.insert(name.to_owned(), machine);
                    answer(vec![("loaded", Value::Bool(true))])
                }
                Err(e) => error(e.message),
            }
        }
        "canonical" => match canonical_context(request) {
            Some(context) => {
                let snapshot = Snapshot {
                    machine: "m".into(),
                    version: 1,
                    state: "S".into(),
                    context,
                };
                answer(vec![("wire", Value::from(canonical_wire(&snapshot)))])
            }
            None => error("a canonical request carries a context object, stringUnits or keyUnits".into()),
        },
        "end" => answer(Vec::new()),
        "plan" => {
            let Some(machine) = name.and_then(|n| machines.get(n)) else {
                return error(format!("no machine {} is loaded", name.unwrap_or_default()));
            };
            let Some(snapshot) = request.get("snapshot").and_then(Snapshot::from_value) else {
                return error("the request's snapshot is not a snapshot".into());
            };
            let Some(target) = request.get("target").and_then(Value::as_str) else {
                return error("a plan names a target".into());
            };
            let fields = match request.get("fields") {
                None | Some(Value::Null) => None,
                Some(fields) => match fields.get("trigger").and_then(Value::as_str) {
                    Some(trigger) => Some(PlanEvent::new(trigger, fields.get("input").cloned())),
                    None => return error("the plan's fields name no trigger".into()),
                },
            };
            let Some(navigation) = request.get("navigation").and_then(Value::as_array).and_then(|n| {
                n.iter()
                    .map(|t| t.as_str().map(str::to_owned))
                    .collect::<Option<Vec<_>>>()
            }) else {
                return error("a plan's navigation is a list of triggers".into());
            };
            let max_depth = match request.get("maxDepth") {
                None => None,
                Some(depth) => match depth.as_f64().filter(|d| d.fract() == 0.0 && *d >= 0.0) {
                    Some(depth) => Some(depth as usize),
                    None => return error("a plan's maxDepth is a whole number".into()),
                },
            };
            answer(plan_members(machine.plan(
                &snapshot,
                target,
                fields.as_ref(),
                &navigation,
                max_depth,
            )))
        }
        "advance" | "outcome" | "timer" | "rehydrate" | "serialize" => {
            let Some(machine) = name.and_then(|n| machines.get(n)) else {
                return error(format!("no machine {} is loaded", name.unwrap_or_default()));
            };
            if op == "rehydrate" {
                let Some(text) = request.get("text").and_then(Value::as_str) else {
                    return error("a rehydrate carries text".into());
                };
                let from_server = request.get("fromServer").and_then(Value::as_bool) == Some(true);
                let read = if from_server {
                    machine.rehydrate_from_server(text)
                } else {
                    machine.rehydrate(text)
                };
                return match read {
                    Ok(snapshot) => answer(vec![
                        ("result", Value::from("ok")),
                        ("wire", Value::from(machine.serialize(&snapshot))),
                    ]),
                    Err(refusal) => answer(vec![
                        ("result", Value::from("error")),
                        ("code", Value::from(refusal.code.as_str())),
                    ]),
                };
            }
            let Some(snapshot) = request.get("snapshot").and_then(Snapshot::from_value) else {
                return error("the request's snapshot is not a snapshot".into());
            };
            if op == "serialize" {
                return answer(vec![("wire", Value::from(machine.serialize(&snapshot)))]);
            }
            let Some(trigger) = request.get("trigger").and_then(Value::as_str) else {
                return error("the request names no trigger".into());
            };
            let input = request.get("input");
            let result = match op {
                "advance" => machine.advance(&snapshot, trigger, input),
                "outcome" => machine.apply_outcome(&snapshot, trigger, input),
                _ => machine.apply_timer(&snapshot, trigger),
            };
            step(machine, result, answer)
        }
        _ => answer(vec![("unsupported", Value::Bool(true))]),
    }
}

fn plan_members(plan: PlanAnswer) -> Vec<(&'static str, Value)> {
    let events = |events: Vec<PlanEvent>| {
        Value::Array(
            events
                .into_iter()
                .map(|e| {
                    let mut event = Map::new();
                    event.insert("trigger".into(), Value::from(e.trigger.as_str()));
                    if let Some(input) = e.input {
                        event.insert("input".into(), input);
                    }
                    Value::Object(event)
                })
                .collect(),
        )
    };
    match plan {
        PlanAnswer::Plan(plan) => vec![("result", Value::from("plan")), ("events", events(plan))],
        PlanAnswer::Unreachable => vec![("result", Value::from("unreachable"))],
        PlanAnswer::Exceeded => vec![("result", Value::from("exceeded"))],
        PlanAnswer::Undetermined { trigger, events: path } => vec![
            ("result", Value::from("undetermined")),
            ("trigger", Value::from(trigger.as_str())),
            ("events", events(path)),
        ],
        PlanAnswer::Invalid(PlanInvalid::Target(target)) => {
            vec![
                ("result", Value::from("invalid")),
                ("target", Value::from(target.as_str())),
            ]
        }
        PlanAnswer::Invalid(PlanInvalid::Trigger(trigger)) => {
            vec![
                ("result", Value::from("invalid")),
                ("trigger", Value::from(trigger.as_str())),
            ]
        }
    }
}

fn step(machine: &Machine, result: Result<Snapshot, Refusal>, answer: impl Fn(Vec<(&str, Value)>) -> Value) -> Value {
    match result {
        Ok(snapshot) => answer(vec![
            ("outcome", Value::from("transitioned")),
            ("wire", Value::from(machine.serialize(&snapshot))),
        ]),
        Err(refusal) => answer(vec![
            ("outcome", Value::from("rejected")),
            ("reason", Value::from(refusal.code.as_str())),
        ]),
    }
}

// The context a canonical request names: JSON text, or a string given as UTF-16 code units, which no JSON parser
// carries when it holds a lone surrogate.
fn canonical_context(request: &Value) -> Option<Map> {
    let units = |key: &str| -> Option<Vec<u16>> {
        request
            .get(key)?
            .as_array()?
            .iter()
            .map(|u| u.as_f64().filter(|n| (0.0..=65535.0).contains(n)).map(|n| n as u16))
            .collect()
    };
    if let Some(text) = request.get("context").and_then(Value::as_str) {
        return Value::parse(text).ok()?.as_object().cloned();
    }
    let mut map = Map::new();
    if let Some(units) = units("stringUnits") {
        map.insert("s".into(), Value::String(JsString::from_units(&units)));
    } else {
        map.insert(JsString::from_units(&units("keyUnits")?), Value::Number(1.0));
    }
    Some(map)
}
