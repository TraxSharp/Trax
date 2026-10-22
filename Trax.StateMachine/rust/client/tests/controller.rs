// The controller's construction and lifetime, through the crate's public API, over a server that answers nothing.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use trax_state_machine::Value;
use trax_state_machine_client::{
    ir_hash, EventRequest, MachineServer, ServerAnswer, ServerMachineController, ServerMachineOptions, StreamSink,
    Subscription, TransportError,
};

// The turnstile's IR as the kit commits it, and the hash a host's manifest gives it: the SHA-256 of that text.
fn turnstile() -> (Value, String) {
    let file = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/machines/turnstile/turnstile.ir.json");
    let text = std::fs::read_to_string(&file).unwrap_or_else(|e| panic!("{}: {e}", file.display()));
    let text = text.trim_end_matches('\n');
    let digest = ring::digest::digest(&ring::digest::SHA256, text.as_bytes());
    let hash = digest.as_ref().iter().map(|b| format!("{b:02x}")).collect();
    (Value::parse(text).unwrap(), hash)
}

// A server that never answers, and says when the last reference to it is dropped.
struct Silent {
    dropped: Arc<AtomicBool>,
}

impl Drop for Silent {
    fn drop(&mut self) {
        self.dropped.store(true, Ordering::SeqCst);
    }
}

struct Nothing;

impl Subscription for Nothing {}

impl MachineServer for Silent {
    async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    async fn event(&self, _: EventRequest) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    async fn get(&self, _: String) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    fn subscribe(&self, _: String, _: StreamSink) -> Box<dyn Subscription> {
        Box::new(Nothing)
    }
}

fn silent() -> (Silent, Arc<AtomicBool>) {
    let dropped = Arc::new(AtomicBool::new(false));
    (
        Silent {
            dropped: dropped.clone(),
        },
        dropped,
    )
}

#[test]
fn the_ir_hash_is_the_hash_of_the_ir_as_the_host_exports_it() {
    let (ir, hash) = turnstile();
    assert_eq!(ir_hash(&ir), hash);
}

#[tokio::test]
async fn an_ir_of_another_version_than_the_manifests_hash_is_refused() {
    let (ir, _) = turnstile();
    let (server, _) = silent();

    let Err(error) = ServerMachineController::new(ServerMachineOptions::new(ir, "0".repeat(64), server)) else {
        panic!("an IR that does not hash to the manifest's irHash was accepted");
    };

    assert!(error.message.contains("irHash"), "{}", error.message);
    assert!(error.message.contains("turnstile"), "{}", error.message);
}

#[tokio::test]
async fn an_ir_that_hashes_to_the_manifests_hash_is_accepted() {
    let (ir, hash) = turnstile();
    let (server, _) = silent();

    assert!(ServerMachineController::new(ServerMachineOptions::new(ir, hash, server)).is_ok());
}
