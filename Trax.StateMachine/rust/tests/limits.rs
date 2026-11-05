// conformance/limits.json holds the limits every engine holds a value to; this holds the Rust engine's to it, as the
// TypeScript and C# tests hold theirs. A value that differs is fixed in the engine, not here.

use std::path::PathBuf;

use trax_state_machine::{Value, MAX_DEPTH, MAX_PLAN_EXPANSIONS};

fn limit(key: &str) -> usize {
    let file = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../conformance/limits.json");
    let text = std::fs::read_to_string(&file).unwrap_or_else(|e| panic!("{}: {e}", file.display()));
    Value::parse(&text)
        .unwrap()
        .get(key)
        .and_then(Value::as_f64)
        .unwrap_or_else(|| panic!("limits.json has no number {key}")) as usize
}

#[test]
fn the_shared_limits_are_the_kits() {
    assert_eq!(MAX_DEPTH, limit("maxValueDepth"));
    assert_eq!(MAX_PLAN_EXPANSIONS, limit("maxPlanExpansions"));
}
