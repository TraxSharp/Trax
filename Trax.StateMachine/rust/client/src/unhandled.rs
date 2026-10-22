//! The events a client cannot predict: those whose outcome rests on a custom rule it holds no handler for.
//!
//! The client builds its machine from the IR alone, so it binds no handler to a custom guard, reduction or state
//! requirement. Without one the engine counts a custom guard as failed and a custom reduction as keeping the context,
//! which is not what the server does: a prediction through one would show the wrong state, or be sent and refused as
//! `CLIENT_DIVERGENCE`. So an event with such an edge out of the state it fires from is sent with no prediction, and
//! nothing after it is predicted until it answers, as an event bound to the effect is. The TypeScript and C# clients
//! read the same edges the same way.

use std::collections::HashSet;

use trax_state_machine::Value;

/// The (state, trigger) pairs with an edge whose guard names a custom rule, whose reduction is custom, or whose
/// destination's requirement names a custom rule.
pub(crate) fn unpredictable_edges(ir: &Value) -> HashSet<(String, String)> {
    let invariants = ir.get("invariants");
    let empty = Vec::new();
    ir.get("transitions")
        .and_then(|t| t.as_array())
        .unwrap_or(&empty)
        .iter()
        .filter(|edge| {
            let custom_guard = edge.get("guard").is_some_and(names_custom);
            let custom_reduction = edge
                .get("reduce")
                .and_then(|r| r.get("reduce"))
                .and_then(|r| r.as_str())
                == Some("custom");
            let custom_requirement = edge
                .get("to")
                .and_then(|to| to.as_str())
                .and_then(|to| invariants.and_then(|i| i.get(to)))
                .is_some_and(names_custom);
            custom_guard || custom_reduction || custom_requirement
        })
        .filter_map(|edge| {
            let from = edge.get("from")?.as_str()?.to_owned();
            let trigger = edge.get("trigger")?.as_str()?.to_owned();
            Some((from, trigger))
        })
        .collect()
}

// Whether a rule is, or holds, a custom rule.
fn names_custom(rule: &Value) -> bool {
    match rule.get("rule").and_then(|r| r.as_str()) {
        Some("custom") => true,
        Some("all") | Some("any") => rule
            .get("rules")
            .and_then(|r| r.as_array())
            .is_some_and(|rules| rules.iter().any(names_custom)),
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_edge_that_rests_on_a_custom_rule_is_unpredictable() {
        let ir = Value::parse(
            r#"{"invariants":{"Closed":{"rule":"all","rules":[{"rule":"custom","name":"c"}]}},"transitions":[
                {"from":"Draft","trigger":"Edit","to":"Draft","reduce":{"reduce":"custom","name":"r"}},
                {"from":"Draft","trigger":"Approve","to":"Approved","guard":{"rule":"any","rules":[{"rule":"custom","name":"g"}]}},
                {"from":"Draft","trigger":"Approve","to":"Escalated"},
                {"from":"Draft","trigger":"Close","to":"Closed"},
                {"from":"Draft","trigger":"Touch","to":"Draft","reduce":{"reduce":"keep"}}]}"#,
        )
        .unwrap();

        let edges = unpredictable_edges(&ir);

        let pair = |s: &str, t: &str| (s.to_owned(), t.to_owned());
        assert_eq!(
            edges,
            [pair("Draft", "Edit"), pair("Draft", "Approve"), pair("Draft", "Close")]
                .into_iter()
                .collect()
        );
    }
}
