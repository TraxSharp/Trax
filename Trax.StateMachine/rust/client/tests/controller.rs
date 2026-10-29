// The controller's construction and lifetime, through the crate's public API, over a server that answers nothing.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

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

// Waits for the controller's task to end: its view's sender goes with it.
async fn task_ends(mut view: tokio::sync::watch::Receiver<Arc<trax_state_machine_client::MachineView>>) -> bool {
    tokio::time::timeout(Duration::from_secs(5), async { while view.changed().await.is_ok() {} })
        .await
        .is_ok()
}

#[tokio::test]
async fn a_dropped_controller_ends_its_task() {
    let (ir, hash) = turnstile();
    let (server, _) = silent();
    let controller = ServerMachineController::new(ServerMachineOptions::new(ir, hash, server)).unwrap();
    let view = controller.watch();

    drop(controller);

    assert!(task_ends(view).await, "the controller's task outlived it");
}

#[tokio::test]
async fn a_disposed_controller_ends_its_task_with_a_request_in_flight() {
    let (ir, hash) = turnstile();
    let (server, _) = silent();
    let mut options = ServerMachineOptions::new(ir, hash, server);
    options.id = Some("i-1".into());
    let controller = ServerMachineController::new(options).unwrap();
    let view = controller.watch();
    let started = controller.start();
    let sent = controller.send("Coin", None);

    controller.dispose();
    started.await;
    let outcome = sent.await;

    assert!(
        matches!(&outcome, trax_state_machine_client::SendOutcome::Failed(p) if p.code == "DISPOSED"),
        "{outcome:?}"
    );
    assert!(task_ends(view).await, "the disposed controller's task kept running");
}

#[test]
fn a_controller_built_outside_a_runtime_with_none_named_is_refused_not_panicked() {
    let (ir, hash) = turnstile();
    let (server, _) = silent();

    let Err(error) = ServerMachineController::new(ServerMachineOptions::new(ir, hash, server)) else {
        panic!("a controller with no runtime to run on was built");
    };

    assert!(error.message.contains("runtime"), "{}", error.message);
}

#[test]
fn a_controller_runs_on_the_runtime_its_options_name_and_is_called_from_any_thread() {
    let runtime = tokio::runtime::Runtime::new().unwrap();
    let (ir, hash) = turnstile();
    let (server, _) = silent();
    let mut options = ServerMachineOptions::new(ir, hash, server);
    options.runtime = Some(runtime.handle().clone());
    let controller = ServerMachineController::new(options).unwrap();
    let view = controller.watch();

    let sent = std::thread::spawn(move || {
        let sent = controller.send("Coin", None);
        controller.dispose();
        sent
    })
    .join()
    .unwrap();

    let outcome = runtime.block_on(sent);
    assert!(
        matches!(&outcome, trax_state_machine_client::SendOutcome::Failed(p) if p.code == "DISPOSED"),
        "{outcome:?}"
    );
    assert!(runtime.block_on(task_ends(view)), "the controller's task kept running");
}

#[tokio::test]
async fn an_input_nested_past_the_limit_is_refused_before_it_is_sent() {
    let (ir, hash) = turnstile();
    let (server, _) = silent();
    let controller = ServerMachineController::new(ServerMachineOptions::new(ir, hash, server)).unwrap();
    let mut input = Value::Null;
    for _ in 0..100_000 {
        input = Value::from(vec![input]);
    }

    let outcome = controller.send("Coin", Some(input)).await;

    assert!(
        matches!(&outcome, trax_state_machine_client::SendOutcome::Refused(p) if p.code == "INVALID_INPUT"),
        "{outcome:?}"
    );
}
