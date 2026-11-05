// conformance/client/defaults.json holds every client default once; this holds the Rust client's to it, as the
// TypeScript and C# tests hold theirs. A value that differs is fixed in the client, not here.

use std::path::PathBuf;
use std::time::Duration;

use trax_state_machine::Value;
use trax_state_machine_client::{
    GraphQLMachineServerOptions, DEFAULT_CONNECT_TIMEOUT, DEFAULT_IDLE_TIMEOUT, DEFAULT_MAX_MESSAGE_SIZE,
    DEFAULT_REQUEST_TIMEOUT, DEFAULT_RESUBSCRIBE_DELAYS, DEFAULT_RETRY_DELAYS,
};

fn defaults() -> Value {
    let file = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/client/defaults.json");
    let text = std::fs::read_to_string(&file).unwrap_or_else(|e| panic!("{}: {e}", file.display()));
    Value::parse(&text).unwrap()
}

fn number(value: &Value, key: &str) -> f64 {
    value
        .get(key)
        .and_then(Value::as_f64)
        .unwrap_or_else(|| panic!("defaults.json has no number {key}"))
}

fn ms(value: &Value, key: &str) -> Duration {
    Duration::from_millis(number(value, key) as u64)
}

fn ms_list(value: &Value, key: &str) -> Vec<Duration> {
    value
        .get(key)
        .and_then(Value::as_array)
        .unwrap_or_else(|| panic!("defaults.json has no list {key}"))
        .iter()
        .map(|d| Duration::from_millis(d.as_f64().unwrap() as u64))
        .collect()
}

#[test]
fn the_request_timeout_and_the_delays_are_the_kits() {
    let kit = defaults();
    assert_eq!(DEFAULT_REQUEST_TIMEOUT, ms(&kit, "requestTimeoutMs"));
    assert_eq!(DEFAULT_RETRY_DELAYS.to_vec(), ms_list(&kit, "retryDelaysMs"));
    assert_eq!(
        DEFAULT_RESUBSCRIBE_DELAYS.to_vec(),
        ms_list(&kit, "resubscribeDelaysMs")
    );
    let options = GraphQLMachineServerOptions::new("https://h/graphql", "wss://h/graphql", "turnstile", Vec::new());
    assert_eq!(options.request_timeout, ms(&kit, "requestTimeoutMs"));
}

// One limit holds a response body and a socket message alike.
#[test]
fn the_response_and_socket_message_sizes_are_the_kits() {
    let kit = defaults();
    assert_eq!(DEFAULT_MAX_MESSAGE_SIZE as f64, number(&kit, "maxResponseBytes"));
    assert_eq!(
        DEFAULT_MAX_MESSAGE_SIZE as f64,
        number(kit.get("maxSocketMessageBytes").unwrap(), "rust")
    );
    let options = GraphQLMachineServerOptions::new("https://h/graphql", "wss://h/graphql", "turnstile", Vec::new());
    assert_eq!(options.max_message_size as f64, number(&kit, "maxResponseBytes"));
}

#[test]
fn the_sockets_timing_is_the_kits() {
    let kit = defaults();
    let timing = kit.get("socket").and_then(|s| s.get("rust")).unwrap();
    assert_eq!(DEFAULT_CONNECT_TIMEOUT, ms(timing, "connectTimeoutMs"));
    assert_eq!(DEFAULT_IDLE_TIMEOUT, ms(timing, "idleTimeoutMs"));
}
