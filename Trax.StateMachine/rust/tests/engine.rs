// What the conformance kit does not send: the refusal-code registry, the migration golden (its v2 machine has no IR,
// so each engine replays it in its own tests), custom rules with and without handlers, and totality on broken input.

use std::path::PathBuf;

use trax_state_machine::{Machine, Map, RefusalCode, Snapshot, Value};

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
fn a_lone_surrogate_names_no_machine_and_no_state() {
    let machine = Machine::from_ir(&conformance("machines/turnstile/turnstile.ir.json")).unwrap();
    let foreign = r#"{"machine":"\ud800","version":1,"state":"Locked","context":{}}"#;
    assert_eq!(
        machine.rehydrate(foreign).unwrap_err().code,
        RefusalCode::UnknownMachine
    );
    let lost = r#"{"machine":"turnstile","version":1,"state":"\ud800","context":{}}"#;
    assert_eq!(machine.rehydrate(lost).unwrap_err().code, RefusalCode::UnknownState);
}
