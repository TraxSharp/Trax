// What the conformance kit does not send: the refusal-code registry, the migration golden (its v2 machine has no IR,
// so each engine replays it in its own tests), custom rules with and without handlers, and totality on broken input.

use std::path::PathBuf;

use trax_state_machine::{JsString, Machine, Map, PlanAnswer, PlanEvent, RefusalCode, Snapshot, Value};

fn conformance(path: &str) -> Value {
    let file = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../conformance")
        .join(path);
    let text = std::fs::read_to_string(&file).unwrap_or_else(|e| panic!("{}: {e}", file.display()));
    Value::parse(&text).unwrap()
}

fn object(text: &str) -> Value {
    Value::parse(text).unwrap()
}

#[test]
fn refusal_codes_are_the_registry_in_its_order() {
    let registry = conformance("refusal-codes.json");
    let codes = registry.get("codes").and_then(Value::as_array).unwrap();
    let listed: Vec<(&str, &str)> = codes
        .iter()
        .map(|c| {
            (
                c.get("name").unwrap().as_str().unwrap(),
                c.get("code").unwrap().as_str().unwrap(),
            )
        })
        .collect();
    let ours: Vec<(&str, &str)> = RefusalCode::ALL.iter().map(|c| (c.name(), c.as_str())).collect();
    assert_eq!(ours, listed);
    for code in RefusalCode::ALL {
        assert_eq!(RefusalCode::from_code(code.as_str()), Some(*code));
    }
}

// The v2 turnstile of the golden: v1 with a boolean `migrated` marker in Unlocked.
fn turnstile_v2() -> Machine {
    let mut ir = conformance("machines/turnstile/turnstile.ir.json");
    let Value::Object(map) = &mut ir else { unreachable!() };
    map.insert("version".into(), Value::Number(2.0));
    let marker = object(r#"{"name":"migrated","type":"boolean","nullable":false,"constraints":[]}"#);
    let Some(Value::Object(context)) = map.get_mut(&"context".into()) else {
        unreachable!()
    };
    let Some(Value::Object(unlocked)) = context.get_mut(&"Unlocked".into()) else {
        unreachable!()
    };
    let Some(Value::Array(fields)) = unlocked.get_mut(&"fields".into()) else {
        unreachable!()
    };
    fields.push(marker);
    Machine::from_ir(&ir).unwrap().with_migration(1, |state, mut context| {
        context.insert("migrated".into(), Value::Bool(true));
        (state.to_owned(), context)
    })
}

#[test]
fn the_migration_golden_replays() {
    let golden = conformance("machines/turnstile/migration.json");
    let machine = turnstile_v2();
    for case in golden.get("cases").and_then(Value::as_array).unwrap() {
        let stored = case.get("stored").unwrap().as_str().unwrap();
        let expected = case.get("expected").unwrap().as_str().unwrap();
        let snapshot = machine.rehydrate(stored).unwrap();
        assert_eq!(machine.serialize(&snapshot), expected);
    }
}

#[test]
fn a_version_with_no_migration_is_a_mismatch() {
    let machine = Machine::from_ir(&{
        let mut ir = conformance("machines/turnstile/turnstile.ir.json");
        if let Value::Object(map) = &mut ir {
            map.insert("version".into(), Value::Number(3.0));
        }
        ir
    })
    .unwrap();
    let stored = r#"{"machine":"turnstile","version":1,"state":"Locked","context":{}}"#;
    assert_eq!(
        machine.rehydrate(stored).unwrap_err().code,
        RefusalCode::VersionMismatch
    );
}

const CUSTOM: &str = r#"{"id":"c","version":1,"initialState":"A","initialContext":{},"states":["A","B"],
  "triggers":["Go"],"committedStates":[],"context":{"A":{"fields":[]},"B":{"fields":[]}},"inputs":{},
  "invariants":{"B":{"rule":"custom","name":"Approved"}},
  "transitions":[{"from":"A","trigger":"Go","to":"B","guard":{"rule":"custom","name":"Open"},
                  "reduce":{"reduce":"custom","name":"Stamp"}}]}"#;

fn start() -> Snapshot {
    Snapshot {
        machine: "c".into(),
        version: 1,
        state: "A".into(),
        context: Map::new(),
    }
}

#[test]
fn a_custom_rule_with_no_handler_is_left_to_the_server() {
    let machine = Machine::from_ir_text(CUSTOM).unwrap();
    assert_eq!(
        machine.advance(&start(), "Go", None).unwrap_err().code,
        RefusalCode::GuardFailed
    );
    assert!(!machine.can_fire(&start(), "Go", None));
    let stored = r#"{"machine":"c","version":1,"state":"B","context":{}}"#;
    assert_eq!(machine.rehydrate(stored).unwrap_err().code, RefusalCode::InvalidContext);
    assert!(machine.rehydrate_from_server(stored).is_ok());
}

#[test]
fn bound_handlers_run() {
    let machine = Machine::from_ir_text(CUSTOM)
        .unwrap()
        .with_guard("Open", |_, _| true)
        .with_guard("Approved", |_, _| true)
        .with_reducer("Stamp", |context, _| context.clone());
    let next = machine.advance(&start(), "Go", None).unwrap();
    assert_eq!(next.state, "B");
    // A reducer the guard let through still has to leave a context the target accepts.
    let strict = Machine::from_ir_text(CUSTOM)
        .unwrap()
        .with_guard("Open", |_, _| true)
        .with_guard("Approved", |_, _| true)
        .with_reducer("Stamp", |_, _| {
            let mut context = Map::new();
            context.insert("extra".into(), Value::Bool(true));
            context
        });
    assert_eq!(
        strict.advance(&start(), "Go", None).unwrap_err().code,
        RefusalCode::InvalidContext
    );
}

#[test]
fn a_panicking_handler_is_a_refusal() {
    let machine = Machine::from_ir_text(CUSTOM)
        .unwrap()
        .with_guard("Open", |_, _| panic!("a handler bug"))
        .with_guard("Approved", |_, _| panic!("a handler bug"));
    assert_eq!(
        machine.advance(&start(), "Go", None).unwrap_err().code,
        RefusalCode::InternalError
    );
    assert!(!machine.can_fire(&start(), "Go", None));
    let stored = r#"{"machine":"c","version":1,"state":"B","context":{}}"#;
    assert_eq!(machine.rehydrate(stored).unwrap_err().code, RefusalCode::Malformed);
}

#[test]
fn an_unknown_ir_is_refused_not_guessed() {
    assert!(Machine::from_ir_text(
        &CUSTOM.replace(r#""rule":"custom","name":"Open""#, r#""rule":"later","name":"Open""#)
    )
    .is_err());
    assert!(Machine::from_ir_text(&CUSTOM.replace(r#""id":"c","#, r#""irFormat":2,"id":"c","#)).is_err());
    assert!(Machine::from_ir_text("[]").is_err());
}

// Every prefix of every stored fixture, and every fixture with each byte replaced, rehydrates to a value: no input
// panics the engine.
#[test]
fn rehydrate_is_total_on_broken_text() {
    let ir = conformance("machines/checkout/checkout.ir.json");
    let machine = Machine::from_ir(&ir).unwrap();
    let dir = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../conformance/machines/checkout/fixtures/rehydrate");
    for entry in std::fs::read_dir(dir).unwrap() {
        let fixture = Value::parse(&std::fs::read_to_string(entry.unwrap().path()).unwrap()).unwrap();
        let text = match fixture.get("raw").and_then(Value::as_str) {
            Some(raw) => raw.to_owned(),
            None => fixture.get("json").unwrap().to_json(),
        };
        for end in 0..=text.len() {
            if text.is_char_boundary(end) {
                let _ = machine.rehydrate(&text[..end]);
            }
        }
        for (i, _) in text.char_indices() {
            for replacement in ["\"", "{", "]", "-", "1e999", "\\ud800", "null"] {
                let broken = format!("{}{replacement}{}", &text[..i], &text[i + 1..]);
                let _ = machine.rehydrate(&broken);
            }
        }
    }
}

#[test]
fn a_lone_surrogate_is_malformed_as_the_csharp_reader_answers() {
    let machine = Machine::from_ir(&conformance("machines/turnstile/turnstile.ir.json")).unwrap();
    for stored in [
        r#"{"machine":"\ud800","version":1,"state":"Locked","context":{}}"#,
        r#"{"machine":"turnstile","version":1,"state":"\ud800","context":{}}"#,
        r#"{"machine":"turnstile","version":1,"state":"Locked","context":{"\ud800":1}}"#,
    ] {
        assert_eq!(
            machine.rehydrate(stored).unwrap_err().code,
            RefusalCode::Malformed,
            "{stored}"
        );
    }
    // A member the reader never reads is not looked at, as there.
    let ignored = r#"{"machine":"turnstile","version":1,"state":"Locked","context":{},"note":"\ud800"}"#;
    assert!(machine.rehydrate(ignored).is_ok());
}

// A value 100 000 arrays deep, built in memory as a binding would build it, where no parser's depth limit applies.
fn too_deep() -> Value {
    let mut value = Value::Null;
    for _ in 0..100_000 {
        value = Value::from(vec![value]);
    }
    value
}

fn turnstile() -> Machine {
    Machine::from_ir(&conformance("machines/turnstile/turnstile.ir.json")).unwrap()
}

#[test]
fn an_input_nested_past_the_limit_is_refused_not_overflowed() {
    let machine = turnstile();
    let input = too_deep();

    let refused = machine.advance(&machine.initial_snapshot(), "Coin", Some(&input));

    assert_eq!(refused.unwrap_err().code, RefusalCode::InvalidInput);
    assert!(!machine.can_fire(&machine.initial_snapshot(), "Coin", Some(&input)));
}

#[test]
fn a_context_nested_past_the_limit_is_refused_not_overflowed() {
    let machine = turnstile();
    let mut snapshot = machine.initial_snapshot();
    snapshot.context.insert("deep".into(), too_deep());

    assert_eq!(
        machine.advance(&snapshot, "Coin", None).unwrap_err().code,
        RefusalCode::InvalidContext
    );
    assert!(!machine.can_fire(&snapshot, "Coin", None));
    let mut stored = Map::new();
    stored.insert("machine".into(), Value::from("turnstile"));
    stored.insert("version".into(), Value::Number(1.0));
    stored.insert("state".into(), Value::from("Locked"));
    stored.insert("context".into(), Value::Object(snapshot.context));
    assert!(Snapshot::from_value(&Value::Object(stored)).is_none());
}

#[test]
fn a_reducer_that_nests_past_the_limit_is_refused_not_overflowed() {
    let machine = Machine::from_ir_text(CUSTOM)
        .unwrap()
        .with_guard("Open", |_, _| true)
        .with_guard("Approved", |_, _| true)
        .with_reducer("Stamp", |_, _| {
            let mut context = Map::new();
            context.insert("deep".into(), too_deep());
            context
        });

    assert_eq!(
        machine.advance(&start(), "Go", None).unwrap_err().code,
        RefusalCode::InvalidContext
    );
}

#[test]
fn an_ir_nested_past_the_limit_is_refused_not_overflowed() {
    let mut ir = conformance("machines/turnstile/turnstile.ir.json");
    let Value::Object(map) = &mut ir else { unreachable!() };
    map.insert("note".into(), too_deep());

    assert!(Machine::from_ir(&ir).is_err());
}

#[test]
fn a_value_nested_past_the_limit_is_written_and_dropped_without_overflowing() {
    let deep = too_deep();
    let json = deep.to_json();
    assert_eq!(json, format!("{}null{}", "[".repeat(100_000), "]".repeat(100_000)));
    drop(deep);
}

#[test]
fn an_edge_rests_on_a_custom_rule_until_every_rule_it_names_has_a_handler() {
    let machine = Machine::from_ir_text(CUSTOM).unwrap();
    assert!(machine.rests_on_unhandled_rule("A", "Go"));
    assert!(!machine.rests_on_unhandled_rule("B", "Go"));
    // The guard and the reduction are bound, but the destination's requirement is not.
    let partly = Machine::from_ir_text(CUSTOM)
        .unwrap()
        .with_guard("Open", |_, _| true)
        .with_reducer("Stamp", |context, _| context.clone());
    assert!(partly.rests_on_unhandled_rule("A", "Go"));
    let bound = partly.with_guard("Approved", |_, _| true);
    assert!(!bound.rests_on_unhandled_rule("A", "Go"));
}

fn wizard() -> Machine {
    Machine::from_ir(&conformance("plan/wizard.ir.json")).unwrap()
}

fn details() -> Snapshot {
    Snapshot::from_value(&object(
        r#"{"machine":"wizard","version":1,"state":"Details","context":{"email":null,"name":null}}"#,
    ))
    .unwrap()
}

fn ada() -> PlanEvent {
    PlanEvent::new("Update", Some(object(r#"{"email":"ada@example.com","name":"Ada"}"#)))
}

#[test]
fn a_custom_guard_with_a_handler_is_planned_through() {
    let machine = wizard().with_guard("approved", |_, _| true);

    let plan = machine.plan(&details(), "Review", Some(&ada()), &["Skip".to_owned()], None);

    assert_eq!(plan, PlanAnswer::Plan(vec![ada(), PlanEvent::new("Skip", None)]));
}

#[test]
fn a_plan_with_no_depth_to_spend_is_exceeded_unless_it_is_already_there() {
    let machine = wizard();
    let navigation = ["Next".to_owned()];

    assert_eq!(
        machine.plan(&details(), "Review", Some(&ada()), &navigation, Some(0)),
        PlanAnswer::Exceeded
    );
    assert_eq!(
        machine.plan(&details(), "Details", None, &navigation, Some(0)),
        PlanAnswer::Plan(Vec::new())
    );
}

// A machine whose one edge counts up forever: every snapshot is new, so only the expansion limit ends the search.
const COUNTER: &str = r#"{"id":"n","version":1,"initialState":"A","initialContext":{"n":0},"states":["A","B"],
  "triggers":["Tick"],"committedStates":[],"inputs":{},
  "context":{"A":{"fields":[{"name":"n","type":"number","nullable":false,"constraints":[]}]},
             "B":{"fields":[{"name":"n","type":"number","nullable":false,"constraints":[]}]}},
  "transitions":[{"from":"A","trigger":"Tick","to":"A","reduce":{"reduce":"custom","name":"Count"}}]}"#;

#[test]
fn a_search_that_never_ends_is_bounded_by_its_expansions() {
    let machine = Machine::from_ir_text(COUNTER)
        .unwrap()
        .with_reducer("Count", |context, _| {
            let mut next = context.clone();
            let n = context.get(&JsString::from("n")).and_then(Value::as_f64).unwrap_or(0.0);
            next.insert("n".into(), Value::Number(n + 1.0));
            next
        });

    let plan = machine.plan(
        &machine.initial_snapshot(),
        "B",
        None,
        &["Tick".to_owned()],
        Some(usize::MAX),
    );

    assert_eq!(plan, PlanAnswer::Exceeded);
}

#[test]
fn a_custom_reduction_on_the_fields_event_never_shows_the_fields_applied() {
    // With no handler the reduction would keep the context, so the start would pass for the goal.
    let machine = Machine::from_ir_text(COUNTER).unwrap();

    let plan = machine.plan(
        &machine.initial_snapshot(),
        "A",
        Some(&PlanEvent::new("Tick", None)),
        &[],
        None,
    );

    assert_eq!(
        plan,
        PlanAnswer::Undetermined {
            trigger: "Tick".into(),
            events: Vec::new()
        }
    );
}

// A machine whose one field is nullable and whose Edit sets it from the input.
const NOTE: &str = r#"{"id":"note","version":1,"initialState":"A","initialContext":{},"states":["A"],
  "triggers":["Edit"],"committedStates":[],
  "context":{"A":{"fields":[{"name":"note","type":"string","nullable":true,"constraints":[]}]}},
  "inputs":{"Edit":{"fields":[{"name":"note","type":"string","nullable":true,"constraints":[]}]}},
  "transitions":[{"from":"A","trigger":"Edit","to":"A","reduce":{"reduce":"set","steps":[{"field":"note","value":{"input":"note"}}]}}]}"#;

#[test]
fn a_member_the_host_omits_as_null_counts_as_null() {
    let machine = Machine::from_ir_text(NOTE).unwrap();
    // The host answers the context without its null members.
    let answered = Snapshot::from_value(&object(r#"{"machine":"note","version":1,"state":"A","context":{}}"#)).unwrap();
    let clear = PlanEvent::new("Edit", Some(object(r#"{"note":null}"#)));

    let plan = machine.plan(&answered, "A", Some(&clear), &[], None);

    assert_eq!(plan, PlanAnswer::Plan(Vec::new()));
}

#[test]
fn an_event_the_machine_does_not_declare_is_refused_from_every_state_as_advance_refuses_it() {
    let machine = Machine::from_ir(&conformance("machines/wait/wait.ir.json")).unwrap();
    let initial = machine.initial_snapshot();

    for (trigger, code) in [
        ("Fetching.done", RefusalCode::OutcomeBound),
        ("Fetching.after", RefusalCode::TimerBound),
        ("Discard", RefusalCode::NoTransition),
        ("Nope", RefusalCode::NoTransition),
    ] {
        assert_eq!(machine.event_refusal(trigger).map(|r| r.code), Some(code), "{trigger}");
        assert_eq!(
            machine.advance(&initial, trigger, None).unwrap_err().code,
            code,
            "{trigger}"
        );
    }
    assert!(machine.event_refusal("Retry").is_none());
    assert!(machine.event_refusal("Start").is_none());
}
