// The controller's construction and lifetime, through the crate's public API, over a server that answers nothing.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use trax_state_machine::Map;
use trax_state_machine::Value;
use trax_state_machine::{PlanEvent, PlanInvalid};
use trax_state_machine_client::{
    ir_hash, DueAt, EventRequest, MachineServer, MachineView, SendOutcome, ServerAnswer, ServerMachineController,
    ServerMachineOptions, ServerProblem, ServerSnapshot, StreamMessage, StreamSink, Subscription, SyncOutcome,
    TransportError,
};

// The turnstile's IR as the kit commits it, and the hash a host's manifest gives it: the SHA-256 of that text.
fn turnstile() -> (Value, String) {
    ir_of("machines/turnstile/turnstile.ir.json")
}

// The planner's wizard, which the sync transcripts drive.
fn wizard() -> (Value, String) {
    ir_of("plan/wizard.ir.json")
}

fn ir_of(path: &str) -> (Value, String) {
    let file = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../conformance")
        .join(path);
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

type Answer = tokio::sync::oneshot::Sender<Result<ServerAnswer, TransportError>>;

// A server whose every read and event waits for the test to answer it, and whose stream the test pushes into.
struct Scripted {
    reads: tokio::sync::mpsc::UnboundedSender<Answer>,
    events: tokio::sync::mpsc::UnboundedSender<(EventRequest, Answer)>,
    sink: Arc<std::sync::Mutex<Option<StreamSink>>>,
}

struct Script {
    reads: tokio::sync::mpsc::UnboundedReceiver<Answer>,
    events: tokio::sync::mpsc::UnboundedReceiver<(EventRequest, Answer)>,
    sink: Arc<std::sync::Mutex<Option<StreamSink>>>,
}

impl Script {
    fn push(&self, message: StreamMessage) {
        let sink = self.sink.lock().unwrap().clone().expect("nothing subscribed");
        sink(message);
    }

    async fn read(&mut self) -> Answer {
        tokio::time::timeout(Duration::from_secs(5), self.reads.recv())
            .await
            .expect("no read was sent")
            .unwrap()
    }

    async fn event(&mut self) -> (EventRequest, Answer) {
        tokio::time::timeout(Duration::from_secs(5), self.events.recv())
            .await
            .expect("no event was sent")
            .unwrap()
    }
}

impl MachineServer for Scripted {
    async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    async fn event(&self, request: EventRequest) -> Result<ServerAnswer, TransportError> {
        let (answer, answered) = tokio::sync::oneshot::channel();
        let _ = self.events.send((request, answer));
        match answered.await {
            Ok(result) => result,
            Err(_) => std::future::pending().await,
        }
    }

    async fn get(&self, _: String) -> Result<ServerAnswer, TransportError> {
        let (answer, answered) = tokio::sync::oneshot::channel();
        let _ = self.reads.send(answer);
        match answered.await {
            Ok(result) => result,
            Err(_) => std::future::pending().await,
        }
    }

    fn subscribe(&self, _: String, sink: StreamSink) -> Box<dyn Subscription> {
        *self.sink.lock().unwrap() = Some(sink);
        Box::new(Nothing)
    }
}

fn scripted() -> (Scripted, Script) {
    let (reads, read) = tokio::sync::mpsc::unbounded_channel();
    let (events, event) = tokio::sync::mpsc::unbounded_channel();
    let sink = Arc::new(std::sync::Mutex::new(None));
    (
        Scripted {
            reads,
            events,
            sink: sink.clone(),
        },
        Script {
            reads: read,
            events: event,
            sink,
        },
    )
}

fn snapshot(row_id: Option<&str>, revision: i64, state: &str) -> ServerSnapshot {
    ServerSnapshot {
        row_id: row_id.map(str::to_owned),
        revision: Some(revision),
        state: state.into(),
        context: Map::new(),
        due_at: DueAt::None,
    }
}

fn stored(snapshot: ServerSnapshot) -> Result<ServerAnswer, TransportError> {
    Ok(ServerAnswer::Stored { id: None, snapshot })
}

// Waits until the view satisfies `wanted`, or fails with the last one seen.
async fn view_where(controller: &ServerMachineController, wanted: impl Fn(&MachineView) -> bool) -> Arc<MachineView> {
    let mut views = controller.watch();
    let found = tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            let view = views.borrow_and_update().clone();
            if wanted(&view) {
                return view;
            }
            if views.changed().await.is_err() {
                return view;
            }
        }
    })
    .await;
    found.unwrap_or_else(|_| panic!("the view never got there: {:?}", controller.view()))
}

// Opens instance i-1 on a scripted server and has it held at row A, revision 10, Locked.
async fn opened() -> (ServerMachineController, Script) {
    let (ir, hash) = turnstile();
    let (server, mut script) = scripted();
    let mut options = ServerMachineOptions::new(ir, hash, server);
    options.id = Some("i-1".into());
    let controller = ServerMachineController::new(options).unwrap();
    let started = controller.start();
    let first = script.read().await;
    script.push(StreamMessage::Changed(snapshot(Some("row-a"), 10, "Locked")));
    let _ = first.send(stored(snapshot(None, 10, "Locked")));
    started.await;
    view_where(&controller, |v| {
        v.row_id.as_deref() == Some("row-a") && v.revision == Some(10)
    })
    .await;
    (controller, script)
}

#[tokio::test]
async fn a_new_incarnation_pushed_during_a_read_is_taken_once_its_row_is_read() {
    let (controller, mut script) = opened().await;

    // The host asks for a reload; while that read is out, the instance is deleted and created again.
    script.push(StreamMessage::Reload);
    let reload = script.read().await;
    script.push(StreamMessage::Changed(snapshot(Some("row-b"), 1, "Locked")));
    // The read was answered by the new row, but its answer carries no row id.
    let _ = reload.send(stored(snapshot(None, 1, "Locked")));
    let again = script.read().await;
    let _ = again.send(stored(snapshot(None, 1, "Locked")));

    let view = view_where(&controller, |v| v.row_id.as_deref() == Some("row-b")).await;
    assert_eq!(view.revision, Some(1));
}

// The view is published on a watch channel: an observer runs on its own task, never on the controller's, so one that
// panics, even while it holds the view, stops only itself.
#[tokio::test]
async fn an_observer_that_panics_does_not_stop_the_controller() {
    let (controller, mut script) = opened().await;
    let mut views = controller.watch();
    let observer = tokio::spawn(async move {
        let _ = views.changed().await;
        let _held = views.borrow();
        panic!("an observer's bug");
    });

    let coin = controller.send("Coin", Some(Value::parse(r#"{"coin":"quarter"}"#).unwrap()));
    let push = controller.send("Push", None);
    assert!(observer.await.unwrap_err().is_panic());
    let (request, answer) = script.event().await;
    assert_eq!(request.trigger, "Coin");
    let mut unlocked = snapshot(None, 11, "Unlocked");
    unlocked.context.insert("paidWith".into(), Value::from("quarter"));
    let _ = answer.send(stored(unlocked));
    let (request, answer) = script.event().await;
    assert_eq!(request.trigger, "Push");
    let _ = answer.send(stored(snapshot(None, 12, "Locked")));

    assert!(matches!(coin.await, SendOutcome::Confirmed(s) if s.state == "Unlocked"));
    assert!(matches!(push.await, SendOutcome::Confirmed(s) if s.state == "Locked"));
    let view = view_where(&controller, |v| v.revision == Some(12)).await;
    assert!(!view.pending);
}

// Opens instance i-1 of the wizard on a scripted server, read as `state` with Ada's fields, or none.
async fn opened_wizard(state: &str, filled: bool) -> (ServerMachineController, Script) {
    let (ir, hash) = wizard();
    let (server, mut script) = scripted();
    let mut options = ServerMachineOptions::new(ir, hash, server);
    options.id = Some("i-1".into());
    let controller = ServerMachineController::new(options).unwrap();
    let started = controller.start();
    let first = script.read().await;
    let _ = first.send(stored(wizard_at(1, state, filled)));
    started.await;
    view_where(&controller, |v| v.revision == Some(1)).await;
    (controller, script)
}

fn wizard_at(revision: i64, state: &str, filled: bool) -> ServerSnapshot {
    let mut snapshot = snapshot(None, revision, state);
    if filled {
        snapshot.context = ada().input.unwrap().as_object().unwrap().clone();
    }
    snapshot
}

fn ada() -> PlanEvent {
    PlanEvent::new(
        "Update",
        Some(Value::parse(r#"{"email":"ada@example.com","name":"Ada"}"#).unwrap()),
    )
}

fn navigation() -> Vec<String> {
    ["Begin", "Next", "Back"].map(str::to_owned).to_vec()
}

#[tokio::test]
async fn a_sync_the_host_never_follows_stops_at_twice_the_states_in_events() {
    let (controller, mut script) = opened_wizard("Start", false).await;

    let synced = controller.sync_to("Review", Some(ada()), navigation());
    // The host takes every Begin and stays where it was, so every plan starts over.
    for revision in 2..10 {
        let (request, answer) = script.event().await;
        assert_eq!(request.trigger, "Begin");
        let _ = answer.send(stored(wizard_at(revision, "Start", false)));
    }

    assert_eq!(synced.await, SyncOutcome::Exceeded);
    assert!(script.events.try_recv().is_err(), "the sync sent past its budget");
    assert!(controller.view().unsaved);
}

#[tokio::test]
async fn a_divergence_is_read_once_and_planned_again() {
    let (controller, mut script) = opened_wizard("Details", true).await;

    let synced = controller.sync_to("Review", Some(ada()), navigation());
    let (request, answer) = script.event().await;
    assert_eq!(request.trigger, "Next");
    let _ = answer.send(Ok(ServerAnswer::Refused(ServerProblem {
        code: "CLIENT_DIVERGENCE".into(),
        message: "No.".into(),
    })));
    let read = script.read().await;
    let _ = read.send(stored(wizard_at(2, "Details", true)));
    let (request, answer) = script.event().await;
    assert_eq!(request.trigger, "Next");
    let _ = answer.send(stored(wizard_at(3, "Review", true)));

    assert!(matches!(synced.await, SyncOutcome::Synced { snapshot, revision: Some(3) } if snapshot.state == "Review"));
    assert!(script.reads.try_recv().is_err(), "the divergence was read twice");
    assert!(!controller.view().unsaved);
}

#[tokio::test]
async fn a_sync_over_a_trigger_bound_to_the_effect_is_invalid_and_sends_nothing() {
    let (controller, mut script) = opened_wizard("Review", true).await;

    let synced = controller.sync_to("Sent", None, vec!["Send".to_owned()]);

    assert_eq!(synced.await, SyncOutcome::Invalid(PlanInvalid::Trigger("Send".into())));
    assert!(script.events.try_recv().is_err());
    assert!(controller.view().unsaved);
}

#[tokio::test]
async fn a_disposed_controller_settles_its_sync_as_disposed() {
    let (controller, mut script) = opened_wizard("Start", false).await;

    let synced = controller.sync_to("Review", Some(ada()), navigation());
    let _in_flight = script.event().await;
    controller.dispose();

    assert!(matches!(synced.await, SyncOutcome::Failed(p) if p.code == "DISPOSED"));
}

#[tokio::test]
async fn a_trigger_the_machine_does_not_declare_is_refused_and_sends_nothing() {
    let (controller, mut script) = opened().await;

    for trigger in ["Discard", "Start", "Get", "Changed", "Nope"] {
        let outcome = tokio::time::timeout(Duration::from_secs(5), controller.send(trigger, None))
            .await
            .unwrap_or_else(|_| panic!("{trigger} was sent"));

        assert!(
            matches!(&outcome, SendOutcome::Refused(p) if p.code == "NO_TRANSITION"),
            "{trigger}: {outcome:?}"
        );
    }
    assert!(
        script.events.try_recv().is_err(),
        "an undeclared trigger reached the server"
    );
    assert_eq!(controller.view().error, None);
}

// A server whose every request panics, as a binding's host callback might, except the read, which answers Locked.
struct Panicking;

impl MachineServer for Panicking {
    async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
        panic!("a host callback's bug")
    }

    async fn event(&self, _: EventRequest) -> Result<ServerAnswer, TransportError> {
        panic!("a host callback's bug")
    }

    async fn get(&self, _: String) -> Result<ServerAnswer, TransportError> {
        stored(snapshot(None, 1, "Locked"))
    }

    fn subscribe(&self, _: String, _: StreamSink) -> Box<dyn Subscription> {
        Box::new(Nothing)
    }
}

#[tokio::test]
async fn a_start_whose_request_panics_settles_as_a_transport_error() {
    let (ir, hash) = turnstile();
    let mut options = ServerMachineOptions::new(ir, hash, Panicking);
    options.retry_delays = Some(Vec::new());
    let controller = ServerMachineController::new(options).unwrap();

    tokio::time::timeout(Duration::from_secs(5), controller.start())
        .await
        .expect("the start never settled");

    let view = controller.view();
    assert_eq!(view.problem.as_ref().map(|p| p.code.as_str()), Some("TRANSPORT_ERROR"));
}

#[tokio::test]
async fn an_event_whose_request_panics_settles_failed_as_a_transport_error() {
    let (ir, hash) = turnstile();
    let mut options = ServerMachineOptions::new(ir, hash, Panicking);
    options.id = Some("i-1".into());
    let controller = ServerMachineController::new(options).unwrap();
    controller.start().await;

    let outcome = tokio::time::timeout(Duration::from_secs(5), controller.send("Push", None))
        .await
        .expect("the send never settled");

    assert!(
        matches!(&outcome, SendOutcome::Failed(p) if p.code == "TRANSPORT_ERROR"),
        "{outcome:?}"
    );
    assert!(!controller.view().pending);
}

// A machine whose plans search widely: ten states any of them moves to, and three numbers each of them sets to one of
// four values, so a plan to the state nothing reaches expands hundreds of snapshots before it is answered.
fn sprawl() -> (Value, String) {
    let states: Vec<String> = (0..10).map(|i| format!("S{i}")).chain(["T".to_owned()]).collect();
    let mut triggers = Vec::new();
    let mut transitions = Vec::new();
    for to in 0..10 {
        triggers.push(format!("\"Go{to}\""));
        for from in 0..10 {
            transitions.push(format!(r#"{{"from":"S{from}","trigger":"Go{to}","to":"S{to}"}}"#));
        }
    }
    for field in 0..3 {
        for value in 0..4 {
            triggers.push(format!("\"F{field}v{value}\""));
            for at in 0..10 {
                transitions.push(format!(
                    r#"{{"from":"S{at}","trigger":"F{field}v{value}","to":"S{at}","reduce":{{"reduce":"set","steps":[{{"field":"f{field}","value":{{"const":{value}}}}}]}}}}"#
                ));
            }
        }
    }
    let fields = (0..3)
        .map(|f| format!(r#"{{"name":"f{f}","type":"number","nullable":false,"constraints":[]}}"#))
        .collect::<Vec<_>>()
        .join(",");
    let context = states
        .iter()
        .map(|s| format!(r#""{s}":{{"fields":[{fields}]}}"#))
        .collect::<Vec<_>>()
        .join(",");
    let text = format!(
        r#"{{"id":"sprawl","version":1,"initialState":"S0","initialContext":{{"f0":0,"f1":0,"f2":0}},"states":[{}],"triggers":[{}],"committedStates":[],"inputs":{{}},"context":{{{context}}},"transitions":[{}]}}"#,
        states.iter().map(|s| format!("\"{s}\"")).collect::<Vec<_>>().join(","),
        triggers.join(","),
        transitions.join(",")
    );
    let ir = Value::parse(&text).unwrap();
    let hash = ir_hash(&ir);
    (ir, hash)
}

// A server that answers every read with the sprawl's start and never answers anything else.
struct Reading;

impl MachineServer for Reading {
    async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    async fn event(&self, _: EventRequest) -> Result<ServerAnswer, TransportError> {
        std::future::pending().await
    }

    async fn get(&self, _: String) -> Result<ServerAnswer, TransportError> {
        let mut at = snapshot(None, 1, "S0");
        for f in 0..3 {
            at.context.insert(format!("f{f}").as_str().into(), Value::Number(0.0));
        }
        stored(at)
    }

    fn subscribe(&self, _: String, _: StreamSink) -> Box<dyn Subscription> {
        Box::new(Nothing)
    }
}

// On a runtime of one thread, the controller's own task answers a call made while a sync plans, before the plan is done.
#[tokio::test(flavor = "current_thread")]
async fn a_sync_plans_off_the_controllers_task() {
    let (ir, hash) = sprawl();
    let mut options = ServerMachineOptions::new(ir, hash, Reading);
    options.id = Some("i-1".into());
    let controller = ServerMachineController::new(options).unwrap();
    controller.start().await;
    let navigation: Vec<String> = (0..10)
        .map(|i| format!("Go{i}"))
        .chain((0..3).flat_map(|f| (0..4).map(move |v| format!("F{f}v{v}"))))
        .collect();

    let synced = controller.sync_to("T", None, navigation);
    let retried = controller.retry();
    tokio::pin!(synced);

    tokio::select! {
        biased;
        outcome = &mut synced => panic!("the sync planned on the controller's task: {outcome:?}"),
        outcome = retried => assert!(matches!(&outcome, SendOutcome::Failed(p) if p.code == "NOTHING_TO_RETRY"), "{outcome:?}"),
    }
    let outcome = tokio::time::timeout(Duration::from_secs(60), synced)
        .await
        .expect("the plan never answered");
    assert!(
        matches!(outcome, SyncOutcome::Unreachable | SyncOutcome::Exceeded),
        "{outcome:?}"
    );
}

#[tokio::test]
async fn start_settles_once_its_first_read_has_settled_not_while_it_is_retried() {
    let (ir, hash) = turnstile();
    let (server, mut script) = scripted();
    let mut options = ServerMachineOptions::new(ir, hash, server);
    options.id = Some("i-1".into());
    options.retry_delays = Some(vec![Duration::from_millis(10)]);
    let controller = ServerMachineController::new(options).unwrap();

    let started = controller.start();
    tokio::pin!(started);
    let first = script.read().await;
    let _ = first.send(Err(TransportError("dropped".into())));
    let second = script.read().await;

    assert!(
        tokio::time::timeout(Duration::from_millis(50), &mut started)
            .await
            .is_err(),
        "start settled while its read was retried"
    );
    let _ = second.send(stored(snapshot(None, 1, "Locked")));
    tokio::time::timeout(Duration::from_secs(5), started)
        .await
        .expect("start never settled");
    assert_eq!(controller.view().revision, Some(1));
}

// A server that refuses every start, and answers nothing else.
struct Refusing;

impl MachineServer for Refusing {
    async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
        Ok(ServerAnswer::Refused(ServerProblem {
            code: "FORBIDDEN".into(),
            message: "Not yours.".into(),
        }))
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

#[tokio::test]
async fn a_send_with_no_instance_to_send_to_settles_not_ready() {
    let (ir, hash) = turnstile();
    let controller = ServerMachineController::new(ServerMachineOptions::new(ir, hash, Refusing)).unwrap();

    let started = controller.start();
    let queued = controller.send("Push", None);
    let synced = controller.sync_to("Unlocked", None, vec!["Coin".to_owned()]);
    started.await;
    let after = controller.send("Push", None);

    let wait = Duration::from_secs(5);
    for outcome in [queued, after] {
        let outcome = tokio::time::timeout(wait, outcome)
            .await
            .expect("the send never settled");
        assert!(
            matches!(&outcome, SendOutcome::Failed(p) if p.code == "NOT_READY"),
            "{outcome:?}"
        );
    }
    let synced = tokio::time::timeout(wait, synced)
        .await
        .expect("the sync never settled");
    assert!(
        matches!(&synced, SyncOutcome::Failed(p) if p.code == "NOT_READY"),
        "{synced:?}"
    );
    assert!(!controller.view().pending);
}
