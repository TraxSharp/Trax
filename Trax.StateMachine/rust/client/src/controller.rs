//! The server-authoritative controller: the server owns the instance, the client only predicts.
//!
//! Every event goes to the server under its own request id, the server's answer (or a pushed snapshot) replaces the
//! prediction, and snapshots are ordered by revision within one incarnation: anything at or below the revision held is
//! dropped, the controller's own answers included. An answer with no revision cannot be ordered: it is taken, and so is
//! whatever comes after it. The same rules as the TypeScript `ServerMachineController` and the
//! C# one, held to the same transcripts by the client conformance kit.
//!
//! All of the controller's state is owned by one task, which handles one message at a time: a call from the
//! application, a server's answer, a pushed change or a retry timer. So the rules read as they do in the other
//! languages, without locks.

use std::collections::hash_map::RandomState;
use std::future::Future;
use std::hash::{BuildHasher, Hasher};
use std::panic::AssertUnwindSafe;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use futures_util::FutureExt;
use tokio::runtime::Handle;
use tokio::sync::{mpsc, oneshot, watch};
use trax_state_machine::{
    canonical_wire, IrError, JsString, Machine, Map, PlanAnswer, PlanEvent, PlanInvalid, Snapshot, Value,
};

use crate::graphql::MAX_DELAY;
use crate::server::{
    DueAt, EventRequest, MachineServer, ServerAnswer, ServerProblem, ServerSnapshot, StreamMessage, Subscription,
    TransportError,
};

/// The codes the client reports of its own, beside the server's refusal codes.
pub mod codes {
    /// The instance was deleted, or the server holds none the caller may read.
    pub const GONE: &str = "GONE";
    /// The request never reached the server, or its answer never came back.
    pub const TRANSPORT_ERROR: &str = "TRANSPORT_ERROR";
    /// A retry was asked for, but nothing has failed since the last one was retried.
    pub const NOTHING_TO_RETRY: &str = "NOTHING_TO_RETRY";
    /// The controller was disposed before the server answered.
    pub const DISPOSED: &str = "DISPOSED";
    /// There is no instance to send to: the start was refused or failed, or the first read gave up.
    pub const NOT_READY: &str = "NOT_READY";
    pub(crate) const CLIENT_OUTDATED: &str = "CLIENT_OUTDATED";
    pub(crate) const SERVER_OUTDATED: &str = "SERVER_OUTDATED";
    pub(crate) const CLIENT_DIVERGENCE: &str = "CLIENT_DIVERGENCE";
    pub(crate) const NOT_FOUND: &str = "NOT_FOUND";
    pub(crate) const UNAUTHENTICATED: &str = "UNAUTHENTICATED";
    pub(crate) const FORBIDDEN: &str = "FORBIDDEN";
    pub(crate) const INVALID_INPUT: &str = "INVALID_INPUT";
    pub(crate) const GUARD_FAILED: &str = "GUARD_FAILED";
    pub(crate) const NO_TRANSITION: &str = "NO_TRANSITION";
    pub(crate) const CONFLICT: &str = "CONFLICT";
    pub(crate) const INVALID_REQUEST_ID: &str = "INVALID_REQUEST_ID";
}

/// Whether a problem is the server's refusal or a request that did not get through.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ProblemKind {
    /// The server, or the client's own engine, refused it.
    Rejected,
    /// The request did not get through, or its answer did not come back.
    Transport,
}

/// The last refusal or transport failure, shown until the next event the server confirms.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MachineProblem {
    /// A refusal or a transport failure.
    pub kind: ProblemKind,
    /// The code, which is contract: `GUARD_FAILED`, `TRANSPORT_ERROR`.
    pub code: String,
    /// What went wrong, for people.
    pub message: String,
}

/// Why a controller stopped for good: the instance is gone, this client is out of date and must reload, or the host
/// refused the socket or the stream.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MachineError {
    /// `GONE`, `CLIENT_OUTDATED`, `UNAUTHENTICATED` or `FORBIDDEN` for a socket or a stream the host refused, or a
    /// stream error's code the server was told is terminal.
    pub code: String,
    /// What stopped it, for people.
    pub message: String,
    /// With `GONE`: true when the instance was deleted, false when the server answered `NOT_FOUND`.
    pub deleted: Option<bool>,
}

/// Where a controller is.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MachineStatus {
    /// Before the first snapshot the server confirmed.
    Loading,
    /// Holding a snapshot, and taking events.
    Ready,
    /// Stopped for good: `error` says why.
    Stopped,
}

/// What a controller holds, as one immutable value: a new one on every change.
#[derive(Clone, Debug, PartialEq)]
pub struct MachineView {
    /// Where the controller is.
    pub status: MachineStatus,
    /// The instance's id, once started or given.
    pub id: Option<String>,
    /// What to show: the newest prediction while an event is pending, else what the server confirmed.
    pub snapshot: Option<Snapshot>,
    /// The last snapshot the server confirmed.
    pub confirmed: Option<Snapshot>,
    /// The row's incarnation: a delete and re-create gives a new one, which restarts `revision`.
    pub row_id: Option<String>,
    /// The revision of the last snapshot the server confirmed.
    pub revision: Option<i64>,
    /// When the state's timer is due, as the server says; `None` when it has none. The client never fires it.
    pub due_at: Option<String>,
    /// Whether an event is in flight or queued.
    pub pending: bool,
    /// The last refusal or transport failure, shown until the next event the server confirms.
    pub problem: Option<MachineProblem>,
    /// Why the controller stopped, once it has.
    pub error: Option<MachineError>,
    /// Whether a sync is running, or the last one did not settle synced: what a page holds a leave guard on.
    pub unsaved: bool,
}

/// What one send came to. Never a panic.
#[derive(Clone, Debug, PartialEq)]
pub enum SendOutcome {
    /// The server applied it and stored this snapshot.
    Confirmed(ServerSnapshot),
    /// The server refused it.
    Refused(MachineProblem),
    /// It did not get through, or the server could not take it yet; `retry` resends it.
    Failed(MachineProblem),
    /// The controller stopped for good before or while it was sent.
    Stopped(MachineError),
}

/// What one [`ServerMachineController::sync_to`] came to. Never a panic.
#[derive(Clone, Debug, PartialEq)]
pub enum SyncOutcome {
    /// The server holds the target state with the fields applied: this snapshot, at this revision.
    Synced {
        /// What the server holds.
        snapshot: Snapshot,
        /// Its revision.
        revision: Option<i64>,
    },
    /// A later sync took over; this one sent nothing after the event it had in flight.
    Superseded,
    /// No event the planner may send reaches the target from what the server holds, or the search broke, as the
    /// TypeScript planner answers one that throws.
    Unreachable,
    /// The planner gave up, or the sync sent twice the machine's states in events without getting there.
    Exceeded,
    /// The target or a trigger is not one a sync may use.
    Invalid(PlanInvalid),
    /// The server refused an event, again after one re-read, or with a code a re-read cannot change.
    Refused(MachineProblem),
    /// An event or the re-read did not get through, or the controller went away (`DISPOSED`), its runtime with it.
    Failed(MachineProblem),
    /// The controller stopped for good.
    Stopped(MachineError),
}

/// How a [`ServerMachineController`] is built.
pub struct ServerMachineOptions<S> {
    /// The machine's IR, as the host's client manifest lists it. The client predicts with the machine built from it.
    pub ir: Value,
    /// The IR's hash as the manifest lists it beside the IR (`irHash`): the lowercase hex SHA-256 of the IR's canonical
    /// JSON. The controller refuses an IR that does not hash to it, since the host would answer every request
    /// `CLIENT_OUTDATED`.
    pub ir_hash: String,
    /// The server's operations and change stream for that machine.
    pub server: S,
    /// An existing instance to open; without one, `start` creates one.
    pub id: Option<String>,
    /// The start's input, when the machine's start takes one.
    pub start_input: Option<Value>,
    /// The request id of the start, minted when `None`.
    pub start_request_id: Option<String>,
    /// Mints a request id: 1 to 200 characters, not blank, no control character. A random one by default. One that
    /// panics (under `panic = "unwind"`) fails the send it was minting for as `INVALID_REQUEST_ID`, or the start, and
    /// sends nothing for it.
    pub new_request_id: Option<Box<dyn FnMut() -> String + Send>>,
    /// Delays between retries of `SERVER_OUTDATED` and of a read the transport failed. `None` for the default. A delay
    /// past 2^31 - 1 ms is refused.
    pub retry_delays: Option<Vec<Duration>>,
    /// The tokio runtime the controller's task and its requests run on. `None` takes the runtime `new` is called
    /// from; a binding that builds the controller on a thread of its host's passes one it owns.
    pub runtime: Option<tokio::runtime::Handle>,
}

impl<S> ServerMachineOptions<S> {
    /// Options for the machine `ir`, whose manifest hash is `ir_hash`, served by `server`, with every other choice left
    /// to its default.
    pub fn new(ir: Value, ir_hash: impl Into<String>, server: S) -> Self {
        ServerMachineOptions {
            ir,
            ir_hash: ir_hash.into(),
            server,
            id: None,
            start_input: None,
            start_request_id: None,
            new_request_id: None,
            retry_delays: None,
            runtime: None,
        }
    }
}

/// The default delays between retries of `SERVER_OUTDATED` or of a failed read.
pub const DEFAULT_RETRY_DELAYS: [Duration; 5] = [
    Duration::from_millis(250),
    Duration::from_millis(500),
    Duration::from_millis(1000),
    Duration::from_millis(2000),
    Duration::from_millis(4000),
];

/// Drives one instance of a machine a server owns. The server applies every transition; the client only predicts,
/// with the machine built from the IR.
///
/// Every operation is total: a refusal or a request that did not get through is a [`SendOutcome`]. Its task runs on
/// the runtime [`ServerMachineOptions::runtime`] names, or the one it is built in; once built, it may be called from any
/// thread. Dropping it disposes it.
pub struct ServerMachineController {
    commands: mpsc::UnboundedSender<Msg>,
    view: watch::Receiver<Arc<MachineView>>,
    machine: Arc<Machine>,
}

impl ServerMachineController {
    /// Builds the controller and starts its task. It sends nothing until [`start`](Self::start). Refuses an IR the
    /// machine cannot be built from, one that does not hash to `ir_hash`, a start input that nests deeper than
    /// [`MAX_DEPTH`](trax_state_machine::MAX_DEPTH), a retry delay past 2^31 - 1 ms, and options with no runtime when it
    /// is not built inside one.
    pub fn new<S: MachineServer>(options: ServerMachineOptions<S>) -> Result<Self, IrError> {
        let Some(runtime) = options.runtime.clone().or_else(|| Handle::try_current().ok()) else {
            return Err(IrError {
                message: "The controller was built outside a tokio runtime and its options name none. Build it inside \
                          one, or pass the runtime's handle as ServerMachineOptions::runtime."
                    .to_owned(),
            });
        };
        if let Some(delay) = options.retry_delays.iter().flatten().find(|d| **d > MAX_DELAY) {
            return Err(IrError {
                message: format!(
                    "The retry delay {} ms is past {} ms, the longest delay a client waits.",
                    delay.as_millis(),
                    MAX_DELAY.as_millis()
                ),
            });
        }
        if options.start_input.as_ref().is_some_and(Value::is_too_deep) {
            return Err(IrError {
                message: format!(
                    "The start input nests deeper than {} arrays and objects.",
                    trax_state_machine::MAX_DEPTH
                ),
            });
        }
        let machine = Arc::new(Machine::from_ir(&options.ir)?);
        let hash = ir_hash(&options.ir);
        if !hash.eq_ignore_ascii_case(options.ir_hash.trim()) {
            return Err(IrError {
                message: format!(
                    "The IR of '{}' hashes to {hash}, not to the manifest's irHash {}: they are of different versions \
                     of the machine. Pass the IR and the irHash of one manifest entry, exported again from a host that \
                     runs this version.",
                    machine.id(),
                    options.ir_hash
                ),
            });
        }
        let (commands, inbox) = mpsc::unbounded_channel();
        let initial = Arc::new(MachineView {
            status: MachineStatus::Loading,
            id: options.id.clone(),
            snapshot: None,
            confirmed: None,
            row_id: None,
            revision: None,
            due_at: None,
            pending: false,
            problem: None,
            error: None,
            unsaved: false,
        });
        let (publish, view) = watch::channel(initial);
        let actor = Actor {
            machine: machine.clone(),
            runtime: runtime.clone(),
            server: Arc::new(options.server),
            commands: commands.clone(),
            publish,
            start_input: options.start_input,
            start_request_id: options.start_request_id,
            new_request_id: options.new_request_id.unwrap_or_else(|| Box::new(random_request_id)),
            retry_delays: options.retry_delays.unwrap_or_else(|| DEFAULT_RETRY_DELAYS.to_vec()),
            id: options.id,
            status: MachineStatus::Loading,
            confirmed: None,
            row_id: None,
            revision: None,
            due_at: None,
            problem: None,
            error: None,
            queue: Vec::new(),
            next_entry: 0,
            sending: false,
            last_failed: None,
            start_failed: false,
            read_failed: false,
            read_waiters: Vec::new(),
            creating: false,
            start_waiters: Vec::new(),
            opened: false,
            open_waiters: Vec::new(),
            opening: false,
            disposed: false,
            stream: None,
            stream_generation: 0,
            epoch: 0,
            reloading: false,
            reload_row_id: None,
            reload_again: false,
            held: false,
            next_row_id: None,
            buffered: None,
            sync: None,
            syncs: 0,
            superseded: Vec::new(),
            unsaved: false,
        };
        runtime.spawn(actor.run(inbox));
        Ok(ServerMachineController {
            commands,
            view,
            machine,
        })
    }

    /// Opens the instance: subscribes first, then reads it, so no write falls between the two. Without an id it starts
    /// one and subscribes to the id the server minted. Resolves once the start or the first read has settled,
    /// after any retry of `SERVER_OUTDATED` or of a failed read, or the controller has stopped.
    pub fn start(&self) -> impl std::future::Future<Output = ()> + Send + 'static {
        let (reply, answer) = oneshot::channel();
        let _ = self.commands.send(Msg::Start(reply));
        async move {
            let _ = answer.await;
        }
    }

    /// Sends one event under a fresh request id, after every earlier event of this instance has settled. The event is
    /// queued when this is called, so two calls send in the order they were made. A trigger the machine does not declare
    /// as an event is refused here, with the code [`Machine::advance`] gives it (`NO_TRANSITION`, `OUTCOME_BOUND` or
    /// `TIMER_BOUND`), and sends nothing. Once `start` has settled with no instance to send to (the start was refused or
    /// failed, or the first read gave up), an event settles failed as `NOT_READY`, and so does one that waited for it.
    pub fn send(
        &self,
        trigger: impl Into<String>,
        input: Option<Value>,
    ) -> impl std::future::Future<Output = SendOutcome> + Send + 'static {
        let (reply, answer) = oneshot::channel();
        let trigger = trigger.into();
        if input.as_ref().is_some_and(Value::is_too_deep) {
            // Refused before the task copies or predicts it: both would recurse over it.
            let _ = reply.send(SendOutcome::Refused(refusal(
                codes::INVALID_INPUT,
                "The input nests deeper than 512 arrays and objects.",
            )));
        } else if let Some(refused) = self.machine.event_refusal(&trigger) {
            // A trigger the machine does not declare as an event never reaches the server: it has no document of its
            // own, and its name may be an operation's.
            let _ = reply.send(SendOutcome::Refused(refusal(refused.code.as_str(), &refused.detail)));
        } else {
            let _ = self.commands.send(Msg::Send { trigger, input, reply });
        }
        async move { answer.await.unwrap_or_else(|_| disposed_outcome()) }
    }

    /// Brings the server to where a page that ran ahead of it is: the state `target`, with the fields event `fields`
    /// applied, moving through the triggers in `navigation`. Plans with the engine's planner ([`Machine::plan`]) from
    /// the snapshot the server last confirmed, sends the first event as [`send`](Self::send) does, and plans again from
    /// each answer until nothing is left to send. A refusal that a re-read can explain (`GUARD_FAILED`,
    /// `NO_TRANSITION`, `CLIENT_DIVERGENCE`, `CONFLICT`) reads the instance once and plans again; a second settles the
    /// sync refused. An undetermined plan sends its events, then its trigger unpredicted. A later sync supersedes this
    /// one once the event it has in flight answers. No sync sends more than twice the machine's states in events. A
    /// fields input nested deeper than [`MAX_DEPTH`](trax_state_machine::MAX_DEPTH) settles it invalid with
    /// [`PlanInvalid::Input`] and sends nothing.
    pub fn sync_to(
        &self,
        target: impl Into<String>,
        fields: Option<PlanEvent>,
        navigation: Vec<String>,
    ) -> impl std::future::Future<Output = SyncOutcome> + Send + 'static {
        let (reply, answer) = oneshot::channel();
        if fields
            .as_ref()
            .and_then(|f| f.input.as_ref())
            .is_some_and(Value::is_too_deep)
        {
            // As the planner answers it, before anything copies the input.
            let _ = reply.send(SyncOutcome::Invalid(PlanInvalid::Input));
        } else {
            let _ = self.commands.send(Msg::Sync {
                target: target.into(),
                fields,
                navigation,
                reply,
            });
        }
        async move { answer.await.unwrap_or_else(|_| SyncOutcome::Failed(disposed_problem())) }
    }

    /// Resends what last failed under the same request id: the start while there is no instance, the first read of an
    /// instance opened by id once it has failed past every retry (settled confirmed with what the read says), else the
    /// last event that failed, unless a later event has been confirmed since, or is queued or in flight now (resending it then would
    /// apply it out of order, predicted from a state the server has moved past): either answers `NOTHING_TO_RETRY`.
    pub fn retry(&self) -> impl std::future::Future<Output = SendOutcome> + Send + 'static {
        let (reply, answer) = oneshot::channel();
        let _ = self.commands.send(Msg::Retry(reply));
        async move { answer.await.unwrap_or_else(|_| disposed_outcome()) }
    }

    /// The current view.
    pub fn view(&self) -> Arc<MachineView> {
        self.view.borrow().clone()
    }

    /// A receiver that changes with every new view.
    pub fn watch(&self) -> watch::Receiver<Arc<MachineView>> {
        self.view.clone()
    }

    /// Whether `trigger` fires from what is shown. False before the first snapshot and once stopped. An event bound to
    /// the effect is judged by its edge alone, since its guard reads an input only the server's effect has, and so is
    /// one whose edge rests on a custom rule the client holds no handler for: the server decides.
    pub fn can_send(&self, trigger: &str, input: Option<&Value>) -> bool {
        let view = self.view();
        match (&view.snapshot, view.status) {
            (Some(shown), status) if status != MachineStatus::Stopped => {
                if self.machine.effect_triggers().iter().any(|t| t == trigger)
                    || self.machine.rests_on_unhandled_rule(&shown.state, trigger)
                {
                    self.machine.available_triggers(shown).iter().any(|t| t == trigger)
                } else {
                    self.machine.can_fire(shown, trigger, input)
                }
            }
            _ => false,
        }
    }

    /// The triggers with an edge out of what is shown; none before the first snapshot or once stopped.
    pub fn available_triggers(&self) -> Vec<String> {
        let view = self.view();
        match (&view.snapshot, view.status) {
            (Some(shown), status) if status != MachineStatus::Stopped => self.machine.available_triggers(shown),
            _ => Vec::new(),
        }
    }

    /// Closes the stream and drops every late answer. Pending sends settle as failed with `DISPOSED`.
    pub fn dispose(&self) {
        let _ = self.commands.send(Msg::Dispose);
    }
}

impl Drop for ServerMachineController {
    fn drop(&mut self) {
        self.dispose();
    }
}

fn disposed_problem() -> MachineProblem {
    refusal(
        codes::DISPOSED,
        "The controller was disposed before the server answered.",
    )
}

fn not_ready_problem() -> MachineProblem {
    refusal(
        codes::NOT_READY,
        "There is no instance to send to: the start was refused or failed, or the first read gave up.",
    )
}

fn unminted_problem() -> MachineProblem {
    refusal(
        codes::INVALID_REQUEST_ID,
        "The request id minter panicked, so nothing was sent.",
    )
}

fn disposed_outcome() -> SendOutcome {
    SendOutcome::Failed(disposed_problem())
}

fn refusal(code: &str, message: &str) -> MachineProblem {
    MachineProblem {
        kind: ProblemKind::Rejected,
        code: code.to_owned(),
        message: message.to_owned(),
    }
}

fn transport_failure(error: &TransportError) -> MachineProblem {
    MachineProblem {
        kind: ProblemKind::Transport,
        code: codes::TRANSPORT_ERROR.to_owned(),
        message: if error.0.is_empty() {
            "The server could not be reached.".to_owned()
        } else {
            error.0.clone()
        },
    }
}

// A server's request that panics (a custom server's bug, or a host callback a binding wraps) answers as one that did not
// get through, so whatever waits on it settles rather than waiting for good. The call itself is made inside it.
async fn guarded(
    request: impl Future<Output = Result<ServerAnswer, TransportError>>,
) -> Result<ServerAnswer, TransportError> {
    AssertUnwindSafe(request).catch_unwind().await.unwrap_or_else(|_| {
        Err(TransportError(
            "The request panicked before it was answered.".to_owned(),
        ))
    })
}

// An answer whose context nests deeper than a store writes a snapshot is not one any store wrote: it fails as a
// transport error, checked where it arrives, before the controller copies it.
fn held_to_store(result: Result<ServerAnswer, TransportError>) -> Result<ServerAnswer, TransportError> {
    match result {
        Ok(ServerAnswer::Stored { snapshot, .. }) if deeper_than_stored(&snapshot.context) => {
            Err(TransportError(too_deep_message()))
        }
        other => other,
    }
}

// The deepest a stored snapshot nests, in arrays and objects, the snapshot itself counting as one: past it no store
// wrote the snapshot, and every engine refuses to read it.
const MAX_STORED_DEPTH: usize = 64;

pub(crate) fn too_deep_message() -> String {
    format!("The server's answer holds a context nested deeper than {MAX_STORED_DEPTH} levels.")
}

// Whether a context nests deeper than a stored snapshot may, the snapshot and the context counting as two levels.
// Never recurses, so it is safe on a context built in memory at any depth.
pub(crate) fn deeper_than_stored(context: &Map) -> bool {
    let mut pending: Vec<(&Value, usize)> = context.values().map(|v| (v, 3)).collect();
    while let Some((value, level)) = pending.pop() {
        let members: Box<dyn Iterator<Item = &Value>> = match value {
            Value::Array(items) => Box::new(items.iter()),
            Value::Object(map) => Box::new(map.values()),
            _ => continue,
        };
        if level > MAX_STORED_DEPTH {
            return true;
        }
        pending.extend(members.map(|v| (v, level + 1)));
    }
    false
}

/// The hash a host's manifest gives an IR: the lowercase hex SHA-256 of its canonical JSON.
pub fn ir_hash(ir: &Value) -> String {
    ring::digest::digest(&ring::digest::SHA256, ir.to_json().as_bytes())
        .as_ref()
        .iter()
        .map(|b| format!("{b:02x}"))
        .collect()
}

static REQUEST_COUNTER: AtomicU64 = AtomicU64::new(0);

// A random request id, unique across processes in practice: two keyed hashes of a counter, as hex.
fn random_request_id() -> String {
    let n = REQUEST_COUNTER.fetch_add(1, Ordering::Relaxed);
    let part = |salt: u64| {
        let mut h = RandomState::new().build_hasher();
        h.write_u64(n);
        h.write_u64(salt);
        h.finish()
    };
    format!("{:016x}{:016x}", part(1), part(2))
}

// ── The task ────────────────────────────────────────────────────────────────────────────────────

enum Msg {
    Start(oneshot::Sender<()>),
    Send {
        trigger: String,
        input: Option<Value>,
        reply: oneshot::Sender<SendOutcome>,
    },
    Retry(oneshot::Sender<SendOutcome>),
    Sync {
        target: String,
        fields: Option<PlanEvent>,
        navigation: Vec<String>,
        reply: oneshot::Sender<SyncOutcome>,
    },
    Dispose,
    Stream {
        generation: u64,
        message: StreamMessage,
    },
    Created {
        request_id: String,
        attempt: usize,
        result: Result<ServerAnswer, TransportError>,
    },
    Read {
        row_id: Option<String>,
        attempt: usize,
        result: Result<ServerAnswer, TransportError>,
    },
    Delivered {
        entry: u64,
        attempt: usize,
        result: Result<ServerAnswer, TransportError>,
    },
    Later {
        epoch: u64,
        action: Later,
    },
    // What a sync's search came to.
    Planned {
        sync: u64,
        searched: Searched,
    },
}

// The stream of a subscribe that panicked: there is nothing to close.
struct NoStream;

impl Subscription for NoStream {}

// What a sync's search, off the controller's task, came to.
enum Searched {
    // Its plan, from the snapshot it was planned from.
    Planned(Snapshot, PlanAnswer),
    // Given up: the sync was taken over or settled, and wants no answer.
    Stopped,
    // The search panicked: a bug in the engine, since a custom handler's panic is caught where it is called.
    Panicked,
    // The runtime shut down before the search ran.
    Cancelled,
}

impl Searched {
    fn of(joined: Result<Option<(Snapshot, PlanAnswer)>, tokio::task::JoinError>) -> Searched {
        match joined {
            Ok(Some((from, answer))) => Searched::Planned(from, answer),
            Ok(None) => Searched::Stopped,
            Err(error) if error.is_panic() => Searched::Panicked,
            Err(_) => Searched::Cancelled,
        }
    }
}

enum Later {
    Create { request_id: String, attempt: usize },
    Read { row_id: Option<String>, attempt: usize },
    Deliver { entry: u64, attempt: usize },
}

struct Queued {
    entry: u64,
    trigger: String,
    input: Option<Value>,
    // Fixed once the event is first sent, so a retry carries exactly the same request.
    request: Option<EventRequest>,
    reply: Option<oneshot::Sender<SendOutcome>>,
}

// The sync running: what it brings the server to, and where it is.
struct SyncRun {
    target: String,
    fields: Option<PlanEvent>,
    navigation: Vec<String>,
    reply: oneshot::Sender<SyncOutcome>,
    // The events it has sent, against its budget of twice the machine's states.
    sent: usize,
    // Whether it has been refused once, and re-read for it.
    refused: bool,
    // The queue entry of its event in flight.
    in_flight: Option<u64>,
    // Whether the event in flight is the fields event.
    sending_fields: bool,
    // The revision the server answered its fields event with, while that is the last event it had confirmed.
    fields_answered: Option<Option<i64>>,
    // Whether it waits for the read after a refusal.
    awaiting_read: bool,
    // Which sync it is, so a plan answered after a later one took over is dropped.
    id: u64,
    // Whether its plan is being searched, off the controller's task.
    planning: bool,
    // Set once the sync is taken over or settles, so a search still running for it gives up.
    stop: Arc<AtomicBool>,
}

// The refusals a re-read can explain: the server holds something other than the client planned from.
const REPLANNED: [&str; 4] = [
    codes::GUARD_FAILED,
    codes::NO_TRANSITION,
    codes::CLIENT_DIVERGENCE,
    codes::CONFLICT,
];

#[derive(Clone, Copy, PartialEq, Eq)]
enum Source {
    Push,
    Answer,
    Load,
}

struct Actor<S> {
    machine: Arc<Machine>,
    runtime: Handle,
    server: Arc<S>,
    commands: mpsc::UnboundedSender<Msg>,
    publish: watch::Sender<Arc<MachineView>>,
    start_input: Option<Value>,
    start_request_id: Option<String>,
    new_request_id: Box<dyn FnMut() -> String + Send>,
    retry_delays: Vec<Duration>,

    id: Option<String>,
    status: MachineStatus,
    confirmed: Option<Snapshot>,
    row_id: Option<String>,
    revision: Option<i64>,
    due_at: Option<String>,
    problem: Option<MachineProblem>,
    error: Option<MachineError>,

    queue: Vec<Queued>,
    next_entry: u64,
    sending: bool,
    last_failed: Option<EventRequest>,
    start_failed: bool,
    // Whether the first read of an instance opened by id failed past every retry, so `retry` reads it again, and who
    // waits for that read.
    read_failed: bool,
    read_waiters: Vec<oneshot::Sender<SendOutcome>>,
    // Whether a start is in flight, or waits to be tried again.
    creating: bool,
    start_waiters: Vec<oneshot::Sender<SendOutcome>>,
    // Whether `start` was called, who waits for the opening to be answered, and whether it still is being answered.
    opened: bool,
    open_waiters: Vec<oneshot::Sender<()>>,
    opening: bool,

    disposed: bool,
    stream: Option<Box<dyn Subscription>>,
    stream_generation: u64,
    // Bumped when the controller stops or is disposed, so a retry timer set before then does nothing.
    epoch: u64,

    reloading: bool,
    reload_row_id: Option<String>,
    reload_again: bool,
    // Whether the queue waits for the reads a CLIENT_DIVERGENCE answer started: the next event is predicted from what
    // they say, not from what the server refused.
    held: bool,
    // The row the read after the one out asks for: a push of another incarnation came while a read without a row id
    // was out.
    next_row_id: Option<String>,
    buffered: Option<ServerSnapshot>,

    sync: Option<SyncRun>,
    // How many syncs have begun: the id of the latest.
    syncs: u64,
    // Syncs a later one took over, by the queue entry of the event each still has in flight.
    superseded: Vec<(u64, oneshot::Sender<SyncOutcome>)>,
    unsaved: bool,
}

impl<S: MachineServer> Actor<S> {
    async fn run(mut self, mut inbox: mpsc::UnboundedReceiver<Msg>) {
        // The task holds a sender of its own for the answers and timers it starts, so the inbox never closes by itself:
        // it ends on dispose, which dropping the controller sends. Every waiter is settled by then; a late answer finds
        // the inbox gone, and a call made after it settles as disposed.
        while let Some(message) = inbox.recv().await {
            self.handle(message);
            if self.disposed {
                self.settle_open();
                return;
            }
        }
    }

    fn handle(&mut self, message: Msg) {
        match message {
            Msg::Start(reply) => {
                if self.disposed || self.error.is_some() || (self.opened && !self.opening) {
                    let _ = reply.send(());
                    return;
                }
                self.open_waiters.push(reply);
                if !self.opened {
                    self.opened = true;
                    self.opening = true;
                    self.open();
                }
            }
            Msg::Send { trigger, input, reply } => {
                if let Some(error) = &self.error {
                    let _ = reply.send(SendOutcome::Stopped(error.clone()));
                } else if self.disposed {
                    let _ = reply.send(disposed_outcome());
                } else if self.not_ready() {
                    let _ = reply.send(SendOutcome::Failed(not_ready_problem()));
                } else {
                    self.enqueue(trigger, input, None, Some(reply));
                }
            }
            Msg::Retry(reply) => self.retry(reply),
            Msg::Sync {
                target,
                fields,
                navigation,
                reply,
            } => self.begin_sync(target, fields, navigation, reply),
            Msg::Dispose => self.dispose(),
            Msg::Stream { generation, message } => {
                if generation == self.stream_generation
                    && self.stream.is_some()
                    && !self.disposed
                    && self.error.is_none()
                {
                    self.on_stream(message);
                }
            }
            Msg::Created {
                request_id,
                attempt,
                result,
            } => {
                // A start retried under its request id has not settled yet: `start` waits for the last attempt.
                if self.on_created(request_id, attempt, result) {
                    self.creating = false;
                    self.settle_open();
                    self.settle_not_ready();
                }
            }
            Msg::Read {
                row_id,
                attempt,
                result,
            } => {
                let failure = match &result {
                    Err(error) => Some(SyncOutcome::Failed(transport_failure(error))),
                    Ok(ServerAnswer::Refused(problem)) if problem.code == codes::SERVER_OUTDATED => {
                        Some(SyncOutcome::Failed(refusal(&problem.code, &problem.message)))
                    }
                    Ok(ServerAnswer::Refused(problem)) => {
                        Some(SyncOutcome::Refused(refusal(&problem.code, &problem.message)))
                    }
                    Ok(ServerAnswer::Stored { .. }) => None,
                };
                // So does a read retried after a transport failure or SERVER_OUTDATED.
                if self.on_read(row_id, attempt, result) {
                    self.settle_open();
                }
                if !self.reloading {
                    self.held = false;
                    self.read_settled(failure);
                    self.settle_not_ready();
                    self.pump();
                }
            }
            Msg::Delivered { entry, attempt, result } => self.on_delivered(entry, attempt, result),
            Msg::Planned { sync, searched } => self.on_planned(sync, searched),
            Msg::Later { epoch, action } => {
                if epoch != self.epoch || self.disposed || self.error.is_some() {
                    return;
                }
                match action {
                    Later::Create { request_id, attempt } => self.create(request_id, attempt),
                    Later::Read { row_id, attempt } => self.read(row_id, attempt),
                    Later::Deliver { entry, attempt } => {
                        if let Some(request) = self
                            .queue
                            .iter()
                            .find(|q| q.entry == entry)
                            .and_then(|q| q.request.clone())
                        {
                            self.deliver(entry, request, attempt);
                        }
                    }
                }
            }
        }
    }

    // Whether there is no instance to send to: `start` was called, and nothing is starting or reading one, but the
    // client holds no snapshot (the start was refused or failed, or the first read gave up). Before `start` is called,
    // an event waits for it.
    fn not_ready(&self) -> bool {
        self.confirmed.is_none() && self.opened && !self.opening && !self.creating && !self.reloading
    }

    // Settles every event waiting to be sent, and the sync, once there is no instance to send them to.
    fn settle_not_ready(&mut self) {
        if self.disposed || self.error.is_some() || !self.not_ready() {
            return;
        }
        let stranded: Vec<Queued> = self.queue.drain(..).collect();
        if !stranded.is_empty() {
            self.emit();
        }
        for queued in stranded {
            if let Some(reply) = queued.reply {
                let _ = reply.send(SendOutcome::Failed(not_ready_problem()));
            }
        }
        self.settle_sync(SyncOutcome::Failed(not_ready_problem()));
        self.settle_superseded();
    }

    // The first answer of the opening (the start or the first read) settles whoever waits on `start`.
    fn settle_open(&mut self) {
        self.opening = false;
        for waiter in self.open_waiters.drain(..) {
            let _ = waiter.send(());
        }
    }

    fn dispose(&mut self) {
        if self.disposed {
            return;
        }
        self.disposed = true;
        self.close();
        self.epoch += 1;
        for queued in self.queue.drain(..) {
            if let Some(reply) = queued.reply {
                let _ = reply.send(disposed_outcome());
            }
        }
        self.settle_start(disposed_outcome());
        self.settle_read(disposed_outcome());
        self.settle_open();
        self.settle_sync(SyncOutcome::Failed(disposed_problem()));
        self.settle_superseded();
    }

    // ── Opening ─────────────────────────────────────────────────────────────────────────────────

    fn open(&mut self) {
        if let Some(id) = self.id.clone() {
            self.listen(id);
            self.read(None, 0);
            return;
        }
        self.start_minted();
    }

    // Starts under the start's request id, minting it first if there is none. A minter that panics fails the start,
    // which `retry` mints for again.
    fn start_minted(&mut self) {
        let request_id = match self.start_request_id.clone() {
            Some(id) => id,
            None => match self.mint() {
                Some(id) => {
                    self.start_request_id = Some(id.clone());
                    id
                }
                None => {
                    let problem = unminted_problem();
                    self.problem = Some(problem.clone());
                    self.start_failed = true;
                    self.emit();
                    self.settle_start(SendOutcome::Failed(problem));
                    self.settle_open();
                    self.settle_not_ready();
                    return;
                }
            },
        };
        self.create(request_id, 0);
    }

    // A request id from the minter, or none when it panicked: the app's bug, which fails the one request it was
    // minting for rather than the controller's task.
    fn mint(&mut self) -> Option<String> {
        let minter = &mut self.new_request_id;
        std::panic::catch_unwind(AssertUnwindSafe(minter)).ok()
    }

    // Calls `start` under one request id for every attempt, `retry` included, so a retry the server already answered
    // replays the instance it created rather than creating another.
    fn create(&mut self, request_id: String, attempt: usize) {
        if self.disposed || self.error.is_some() {
            return;
        }
        self.creating = true;
        let server = self.server.clone();
        let commands = self.commands.clone();
        let input = self.start_input.clone();
        self.runtime.spawn(async move {
            let result = held_to_store(guarded(async { server.start(input, request_id.clone()).await }).await);
            let _ = commands.send(Msg::Created {
                request_id,
                attempt,
                result,
            });
        });
    }

    // Returns false when the start is tried again, true when it has settled.
    fn on_created(&mut self, request_id: String, attempt: usize, result: Result<ServerAnswer, TransportError>) -> bool {
        if self.disposed || self.error.is_some() {
            return true;
        }
        match result {
            Err(error) => {
                self.problem = Some(transport_failure(&error));
                self.start_failed = true;
                self.emit();
                let problem = self.problem.clone().unwrap();
                self.settle_start(SendOutcome::Failed(problem));
            }
            Ok(ServerAnswer::Refused(problem)) => {
                if problem.code == codes::SERVER_OUTDATED && attempt < self.retry_delays.len() {
                    // Another node of a rolling deploy answered: another may run this version.
                    self.later(
                        attempt,
                        Later::Create {
                            request_id,
                            attempt: attempt + 1,
                        },
                    );
                    return false;
                }
                if problem.code == codes::CLIENT_OUTDATED {
                    self.stop(MachineError {
                        code: codes::CLIENT_OUTDATED.to_owned(),
                        message: problem.message,
                        deleted: None,
                    });
                    let error = self.error.clone().unwrap();
                    self.settle_start(SendOutcome::Stopped(error));
                    return true;
                }
                let retryable = problem.code == codes::SERVER_OUTDATED;
                let p = refusal(&problem.code, &problem.message);
                self.problem = Some(p.clone());
                self.start_failed = retryable;
                self.emit();
                self.settle_start(if retryable {
                    SendOutcome::Failed(p)
                } else {
                    SendOutcome::Refused(p)
                });
            }
            Ok(ServerAnswer::Stored { id, snapshot }) => {
                let Some(id) = id else {
                    let problem = transport_failure(&TransportError(
                        "The server started an instance without answering its id.".to_owned(),
                    ));
                    self.problem = Some(problem.clone());
                    self.start_failed = true;
                    self.emit();
                    self.settle_start(SendOutcome::Failed(problem));
                    return true;
                };
                self.id = Some(id.clone());
                self.problem = None;
                self.listen(id);
                // The stream's first event is the instance as it is then, so a write between the answer and the
                // subscription reaches it there; the answer is the snapshot to show until then.
                self.accept(snapshot.clone(), Source::Answer);
                self.emit();
                self.settle_start(SendOutcome::Confirmed(snapshot));
                self.pump();
            }
        }
        true
    }

    fn settle_start(&mut self, outcome: SendOutcome) {
        for waiter in self.start_waiters.drain(..) {
            let _ = waiter.send(outcome.clone());
        }
    }

    fn settle_read(&mut self, outcome: SendOutcome) {
        for waiter in self.read_waiters.drain(..) {
            let _ = waiter.send(outcome.clone());
        }
    }

    // A read that failed past every retry: one that was the first leaves `retry` something to resend.
    fn read_gave_up(&mut self, problem: MachineProblem) {
        self.read_failed = self.confirmed.is_none();
        self.settle_read(SendOutcome::Failed(problem));
    }

    fn listen(&mut self, id: String) {
        self.stream_generation += 1;
        let generation = self.stream_generation;
        let commands = self.commands.clone();
        let refused = commands.clone();
        let sink: crate::server::StreamSink = Arc::new(move |message| {
            // A push no store could have written is dropped here, on the server's task, before the controller copies it,
            // and the instance read again.
            let message = match message {
                StreamMessage::Changed(s) if deeper_than_stored(&s.context) => StreamMessage::Reload,
                message => message,
            };
            let _ = commands.send(Msg::Stream { generation, message });
        });
        let server = &self.server;
        match std::panic::catch_unwind(AssertUnwindSafe(|| server.subscribe(id, sink))) {
            Ok(stream) => self.stream = Some(stream),
            Err(_) => {
                // A server whose subscribe panics (a custom server's bug) has no stream to follow: the controller stops
                // as it does on a stream the host refused, once whatever opened it has run, and every waiter settles
                // with that error.
                self.stream = Some(Box::new(NoStream));
                let _ = refused.send(Msg::Stream {
                    generation,
                    message: StreamMessage::Refused(ServerProblem {
                        code: codes::TRANSPORT_ERROR.to_owned(),
                        message: "The change stream could not be opened: the server's subscribe panicked.".to_owned(),
                    }),
                });
            }
        }
    }

    fn close(&mut self) {
        self.stream = None;
        self.stream_generation += 1;
    }

    fn on_stream(&mut self, message: StreamMessage) {
        match message {
            StreamMessage::Changed(snapshot) => {
                if self.accept(snapshot, Source::Push) {
                    self.emit();
                }
                self.pump();
            }
            StreamMessage::Gone { not_found } => self.stop(if not_found {
                MachineError {
                    code: codes::GONE.to_owned(),
                    message: "The server holds no such instance for this caller.".to_owned(),
                    deleted: Some(false),
                }
            } else {
                MachineError {
                    code: codes::GONE.to_owned(),
                    message: "The instance was deleted or expired.".to_owned(),
                    deleted: Some(true),
                }
            }),
            StreamMessage::ClientOutdated => self.stop(MachineError {
                code: codes::CLIENT_OUTDATED.to_owned(),
                message: "The server no longer serves this version of the machine. Reload.".to_owned(),
                deleted: None,
            }),
            StreamMessage::Refused(refusal) => self.stop(MachineError {
                code: refusal.code,
                message: refusal.message,
                deleted: None,
            }),
            StreamMessage::Reload => self.reload(None),
        }
    }

    // ── Reading ─────────────────────────────────────────────────────────────────────────────────

    fn reload(&mut self, row_id: Option<String>) {
        if self.reloading {
            self.reload_again = true;
            return;
        }
        self.reload_row_id = row_id.clone();
        self.read(row_id, 0);
    }

    fn read(&mut self, row_id: Option<String>, attempt: usize) {
        if self.disposed || self.error.is_some() {
            return;
        }
        let Some(id) = self.id.clone() else { return };
        self.reloading = true;
        self.reload_row_id = row_id.clone();
        let server = self.server.clone();
        let commands = self.commands.clone();
        self.runtime.spawn(async move {
            let result = held_to_store(guarded(async { server.get(id).await }).await);
            let _ = commands.send(Msg::Read {
                row_id,
                attempt,
                result,
            });
        });
    }

    // Returns false when the read is tried again, true when it has settled.
    fn on_read(
        &mut self,
        row_id: Option<String>,
        attempt: usize,
        result: Result<ServerAnswer, TransportError>,
    ) -> bool {
        if self.disposed || self.error.is_some() {
            return true;
        }
        match result {
            Err(error) => {
                self.problem = Some(transport_failure(&error));
                self.emit();
                if attempt < self.retry_delays.len() {
                    self.later(
                        attempt,
                        Later::Read {
                            row_id,
                            attempt: attempt + 1,
                        },
                    );
                    return false;
                }
                self.end_read();
                self.read_gave_up(transport_failure(&error));
            }
            Ok(ServerAnswer::Refused(problem)) => {
                if problem.code == codes::SERVER_OUTDATED && attempt < self.retry_delays.len() {
                    self.later(
                        attempt,
                        Later::Read {
                            row_id,
                            attempt: attempt + 1,
                        },
                    );
                    return false;
                }
                self.end_read();
                if problem.code == codes::CLIENT_OUTDATED {
                    self.stop(MachineError {
                        code: codes::CLIENT_OUTDATED.to_owned(),
                        message: problem.message,
                        deleted: None,
                    });
                } else if problem.code == codes::NOT_FOUND {
                    self.stop(MachineError {
                        code: codes::GONE.to_owned(),
                        message: problem.message,
                        deleted: Some(false),
                    });
                } else {
                    let p = refusal(&problem.code, &problem.message);
                    self.problem = Some(p.clone());
                    self.emit();
                    if problem.code == codes::SERVER_OUTDATED {
                        self.read_gave_up(p);
                    } else {
                        self.settle_read(SendOutcome::Refused(p));
                    }
                }
            }
            Ok(ServerAnswer::Stored { snapshot, .. }) => {
                let row = snapshot
                    .row_id
                    .clone()
                    .or(row_id.clone())
                    .or_else(|| self.row_id.clone());
                let read = ServerSnapshot {
                    row_id: row,
                    ..snapshot
                };
                let mut changed = self.accept(read.clone(), Source::Load);
                self.settle_read(match &self.error {
                    Some(error) => SendOutcome::Stopped(error.clone()),
                    None => SendOutcome::Confirmed(read),
                });
                if let Some(buffered) = self.buffered.take() {
                    if self.error.is_none() && buffered.row_id == self.row_id {
                        changed = self.accept(buffered, Source::Push) || changed;
                    } else if row_id.is_none() && buffered.row_id.is_some() && buffered.row_id == self.next_row_id {
                        // A read without a row id cannot answer for the push's incarnation: it waits for the read that
                        // asks for its row.
                        self.buffered = Some(buffered);
                    }
                }
                self.end_read();
                if changed && self.error.is_none() {
                    self.emit();
                }
                self.pump();
            }
        }
        true
    }

    fn end_read(&mut self) {
        self.reloading = false;
        self.reload_row_id = None;
        let row_id = self.next_row_id.take();
        if self.reload_again && !self.disposed && self.error.is_none() {
            self.reload_again = false;
            self.reload(row_id);
        }
    }

    // ── Ordering ────────────────────────────────────────────────────────────────────────────────

    // Takes a server snapshot if it is newer than the one held; returns whether the view changed. A push of another
    // incarnation than the one held starts one read instead, and is kept until that read answers.
    fn accept(&mut self, incoming: ServerSnapshot, source: Source) -> bool {
        let other_incarnation = matches!((&incoming.row_id, &self.row_id), (Some(a), Some(b)) if a != b);

        if other_incarnation && source != Source::Load {
            // The row was deleted and created again: its revisions restart, so they say nothing against the ones held.
            if !self.reloading {
                self.reload(incoming.row_id.clone());
            } else if self.reload_row_id.is_none() {
                // The read out was asked for without a row id, and its answer carries none: it would be taken as the
                // incarnation held and dropped as stale. The read after it asks for this row instead.
                self.next_row_id = incoming.row_id.clone();
                self.reload_again = true;
            }
            let newer = match &self.buffered {
                Some(b) => b.row_id != incoming.row_id || !older_or_same(incoming.revision, b.revision),
                None => true,
            };
            if newer {
                self.buffered = Some(incoming);
            }
            return false;
        }
        if let Some(held) = self.revision {
            if !other_incarnation && older_or_same(incoming.revision, Some(held)) {
                // A read of the revision held, asked for because an answer did not know the due time of its timed
                // state, brings the due time and nothing else.
                if source == Source::Load && incoming.revision == Some(held) && self.due_at.is_none() {
                    if let DueAt::At(due) = &incoming.due_at {
                        if self.confirmed.as_ref().map(|c| c.state.as_str()) == Some(incoming.state.as_str()) {
                            self.due_at = Some(due.clone());
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        let snapshot = Snapshot {
            machine: self.machine.id().to_owned(),
            version: self.machine.version(),
            state: incoming.state.clone(),
            context: incoming.context.clone(),
        };
        // The server applied every transition and checked the result, custom rules included: the machine trusts the
        // ones it has no handler for, and checks the shape and every built-in rule.
        let parsed = match self.machine.rehydrate_from_server(&canonical_wire(&snapshot)) {
            Ok(parsed) => parsed,
            Err(refused) => {
                // The server serves a snapshot this client's machine cannot read: it was built for another version.
                self.stop(MachineError {
                    code: codes::CLIENT_OUTDATED.to_owned(),
                    message: format!(
                        "The server's snapshot is not one this client's machine can read ({}): {}",
                        refused.code.as_str(),
                        refused.detail
                    ),
                    deleted: None,
                });
                return false;
            }
        };

        // The due time is the server's: a known one replaces the one held, none included. An answer that does not know
        // it keeps the one held only while the instance stays in the state it was given for; in any other state a timed
        // state is read once for it.
        let same_state = !other_incarnation
            && self
                .confirmed
                .as_ref()
                .map(|c| c.state == parsed.state)
                .unwrap_or(false);
        let timed = self.machine.timer(&parsed).is_some();
        match &incoming.due_at {
            DueAt::At(due) => self.due_at = Some(due.clone()),
            DueAt::None => self.due_at = None,
            DueAt::Unknown => {
                if !(same_state && self.due_at.is_some()) {
                    self.due_at = None;
                    if timed && source != Source::Load {
                        self.reload(None);
                    }
                }
            }
        }
        self.confirmed = Some(parsed);
        if incoming.row_id.is_some() {
            self.row_id = incoming.row_id;
        }
        self.revision = incoming.revision;
        if self.status == MachineStatus::Loading {
            self.status = MachineStatus::Ready;
        }
        true
    }

    // ── Sending ─────────────────────────────────────────────────────────────────────────────────

    fn enqueue(
        &mut self,
        trigger: String,
        input: Option<Value>,
        request: Option<EventRequest>,
        reply: Option<oneshot::Sender<SendOutcome>>,
    ) -> u64 {
        let entry = self.queued(trigger, input, request, reply);
        self.emit();
        self.pump();
        entry
    }

    // Puts an event at the back of the queue without sending anything, so a caller can note its entry before `pump`
    // answers it.
    fn queued(
        &mut self,
        trigger: String,
        input: Option<Value>,
        request: Option<EventRequest>,
        reply: Option<oneshot::Sender<SendOutcome>>,
    ) -> u64 {
        self.next_entry += 1;
        let entry = self.next_entry;
        self.queue.push(Queued {
            entry,
            trigger,
            input,
            request,
            reply,
        });
        entry
    }

    fn retry(&mut self, reply: oneshot::Sender<SendOutcome>) {
        if let Some(error) = &self.error {
            let _ = reply.send(SendOutcome::Stopped(error.clone()));
            return;
        }
        if self.disposed {
            let _ = reply.send(disposed_outcome());
            return;
        }
        if self.start_failed && self.id.is_none() {
            self.start_failed = false;
            self.start_waiters.push(reply);
            self.start_minted();
            return;
        }
        // Opened by id, with a first read that failed past every retry: the read is what failed, so it is read again.
        if self.read_failed && self.confirmed.is_none() && !self.reloading {
            self.read_failed = false;
            self.read_waiters.push(reply);
            self.read(None, 0);
            return;
        }
        match self.last_failed.take() {
            None => {
                let _ = reply.send(SendOutcome::Failed(refusal(
                    codes::NOTHING_TO_RETRY,
                    "No event has failed since the last one was retried.",
                )));
            }
            // Every event in the queue was sent after the one that failed; it would go out behind them.
            Some(request) if !self.queue.is_empty() => {
                self.last_failed = Some(request);
                let _ = reply.send(SendOutcome::Failed(refusal(
                    codes::NOTHING_TO_RETRY,
                    "A later event is still being sent; the failed one cannot be resent after it.",
                )));
            }
            Some(request) => {
                let (trigger, input) = (request.trigger.clone(), request.input.clone());
                self.enqueue(trigger, input, Some(request), Some(reply));
            }
        }
    }

    fn is_effect(&self, trigger: &str) -> bool {
        self.machine.effect_triggers().iter().any(|t| t == trigger)
    }

    // Whether the event's outcome from `state` is one the client can tell: not bound to the effect, and with no edge
    // that rests on a custom rule it holds no handler for. It builds its machine from the IR alone, so it binds no
    // handler, and the engine would count a custom guard as failed and a custom reduction as keeping the context: a
    // prediction through one would show the wrong state, or be sent and refused as CLIENT_DIVERGENCE.
    fn predicts(&self, state: &str, trigger: &str) -> bool {
        !self.is_effect(trigger) && !self.machine.rests_on_unhandled_rule(state, trigger)
    }

    fn pump(&mut self) {
        loop {
            if self.sending
                || self.held
                || self.disposed
                || self.error.is_some()
                || self.id.is_none()
                || self.confirmed.is_none()
            {
                return;
            }
            let Some(next) = self.queue.first() else {
                // Nothing is being sent: a sync plans its next event from what the server confirmed.
                self.step_sync();
                return;
            };
            let entry = next.entry;
            let request = match &next.request {
                Some(request) => request.clone(),
                None => match self.first_request() {
                    Some(request) => {
                        self.queue[0].request = Some(request.clone());
                        request
                    }
                    None => {
                        // The minter panicked: that event fails, and the next one is minted for in its turn.
                        let outcome = SendOutcome::Failed(unminted_problem());
                        let reply = self.queue.remove(0).reply;
                        self.emit();
                        self.sync_answered(entry, &outcome);
                        if let Some(reply) = reply {
                            let _ = reply.send(outcome);
                        }
                        continue;
                    }
                },
            };
            self.sending = true;
            self.deliver(entry, request, 0);
            return;
        }
    }

    // The request that first sends the event at the head of the queue, with its prediction from what the server
    // confirmed. An event bound to the effect is sent without one: what it leads to depends on the effect. So is one
    // that rests on a custom rule the client cannot evaluate, rather than a guess the server would refuse.
    fn first_request(&mut self) -> Option<EventRequest> {
        let request_id = self.mint()?;
        let next = &self.queue[0];
        let predicted = self.confirmed.as_ref().and_then(|confirmed| {
            self.predicts(&confirmed.state, &next.trigger)
                .then(|| self.machine.advance(confirmed, &next.trigger, next.input.as_ref()).ok())
                .flatten()
        });
        Some(EventRequest {
            id: self.id.clone().unwrap_or_default(),
            trigger: next.trigger.clone(),
            input: next.input.clone(),
            request_id,
            client_result: predicted.as_ref().map(|s| self.machine.serialize(s)),
        })
    }

    fn deliver(&mut self, entry: u64, request: EventRequest, attempt: usize) {
        let server = self.server.clone();
        let commands = self.commands.clone();
        self.runtime.spawn(async move {
            let result = held_to_store(guarded(async { server.event(request).await }).await);
            let _ = commands.send(Msg::Delivered { entry, attempt, result });
        });
    }

    fn on_delivered(&mut self, entry: u64, attempt: usize, result: Result<ServerAnswer, TransportError>) {
        if self.disposed || self.error.is_some() {
            return;
        }
        let Some(request) = self
            .queue
            .iter()
            .find(|q| q.entry == entry)
            .and_then(|q| q.request.clone())
        else {
            return;
        };
        match result {
            Err(error) => {
                self.last_failed = Some(request);
                let problem = transport_failure(&error);
                self.problem = Some(problem.clone());
                self.finish(entry, SendOutcome::Failed(problem));
            }
            Ok(ServerAnswer::Stored { snapshot, .. }) => {
                self.accept(snapshot.clone(), Source::Answer);
                // A confirmed event supersedes one that failed before it: resending that one now would apply it out of
                // order.
                self.last_failed = None;
                self.problem = None;
                self.finish(entry, SendOutcome::Confirmed(snapshot));
            }
            Ok(ServerAnswer::Refused(ServerProblem { code, message })) => {
                if code == codes::CLIENT_OUTDATED {
                    self.stop(MachineError {
                        code,
                        message,
                        deleted: None,
                    });
                    return;
                }
                if code == codes::SERVER_OUTDATED {
                    if attempt < self.retry_delays.len() {
                        self.later(
                            attempt,
                            Later::Deliver {
                                entry,
                                attempt: attempt + 1,
                            },
                        );
                        return;
                    }
                    self.last_failed = Some(request);
                    let problem = refusal(&code, &message);
                    self.problem = Some(problem.clone());
                    self.finish(entry, SendOutcome::Failed(problem));
                    return;
                }
                if code == codes::CLIENT_DIVERGENCE {
                    // The client and the server disagree about this step: what the client holds cannot be trusted, so
                    // nothing more is sent until it has been read again.
                    self.held = true;
                    self.reload(None);
                }
                let problem = refusal(&code, &message);
                self.problem = Some(problem.clone());
                self.finish(entry, SendOutcome::Refused(problem));
            }
        }
    }

    fn finish(&mut self, entry: u64, outcome: SendOutcome) {
        let reply = self
            .queue
            .iter()
            .position(|q| q.entry == entry)
            .and_then(|i| self.queue.remove(i).reply);
        self.sending = false;
        self.emit();
        self.sync_answered(entry, &outcome);
        if let Some(reply) = reply {
            let _ = reply.send(outcome);
        }
        self.pump();
    }

    // ── Syncing ─────────────────────────────────────────────────────────────────────────────────────

    fn begin_sync(
        &mut self,
        target: String,
        fields: Option<PlanEvent>,
        navigation: Vec<String>,
        reply: oneshot::Sender<SyncOutcome>,
    ) {
        if let Some(error) = &self.error {
            let _ = reply.send(SyncOutcome::Stopped(error.clone()));
            return;
        }
        if self.disposed {
            let _ = reply.send(SyncOutcome::Failed(disposed_problem()));
            return;
        }
        if self.not_ready() {
            let _ = reply.send(SyncOutcome::Failed(not_ready_problem()));
            return;
        }
        // The sync it takes over from settles once its event in flight answers, and sends nothing more. A read that one
        // waits for is waited for here too: the server has refused what the client planned from.
        let mut awaiting_read = false;
        self.syncs += 1;
        if let Some(older) = self.sync.take() {
            older.stop.store(true, Ordering::Relaxed);
            awaiting_read = older.awaiting_read && self.reloading;
            match older.in_flight {
                Some(entry) => self.superseded.push((entry, older.reply)),
                None => {
                    let _ = older.reply.send(SyncOutcome::Superseded);
                }
            }
        }
        self.sync = Some(SyncRun {
            target,
            fields: fields.map(|f| PlanEvent::new(f.trigger, f.input)),
            navigation,
            reply,
            sent: 0,
            refused: false,
            in_flight: None,
            sending_fields: false,
            fields_answered: None,
            awaiting_read,
            id: self.syncs,
            planning: false,
            stop: Arc::default(),
        });
        if !self.unsaved {
            self.unsaved = true;
            self.emit();
        }
        self.pump();
    }

    // Plans from what the server confirmed, or settles the sync. Runs only while nothing is queued or in flight. The
    // search runs off the controller's task, on the runtime's blocking threads: a wide one would otherwise hold every
    // other message, and on a runtime of one thread the socket's pings with it.
    fn step_sync(&mut self) {
        let (Some(run), Some(confirmed)) = (self.sync.as_mut(), self.confirmed.as_ref()) else {
            return;
        };
        if run.in_flight.is_some() || run.awaiting_read || run.planning {
            return;
        }
        run.planning = true;
        let sync = run.id;
        let machine = self.machine.clone();
        let from = confirmed.clone();
        let (target, fields, navigation) = (run.target.clone(), run.fields.clone(), run.navigation.clone());
        let stop = run.stop.clone();
        let search = self.runtime.spawn_blocking(move || {
            let answer = machine.plan_stoppable(&from, &target, fields.as_ref(), &navigation, None, &stop);
            answer.map(|answer| (from, answer))
        });
        let commands = self.commands.clone();
        self.runtime.spawn(async move {
            let _ = commands.send(Msg::Planned {
                sync,
                searched: Searched::of(search.await),
            });
        });
    }

    // A sync's plan has been answered: it sends the plan's first event, or settles. A plan from a snapshot the server
    // has moved past since, or answered while an event went out, is searched again from where things are now.
    fn on_planned(&mut self, sync: u64, searched: Searched) {
        if self.disposed || self.error.is_some() {
            return;
        }
        let Some(run) = self.sync.as_mut().filter(|run| run.id == sync) else {
            return;
        };
        run.planning = false;
        let (confirmed, answer) = match searched {
            Searched::Planned(confirmed, answer) => (confirmed, answer),
            // Only a sync that has gone stops its search, and it is not this one.
            Searched::Stopped => return,
            // A search that breaks reaches nothing, as the TypeScript planner answers one that throws. The server
            // refused nothing and nothing failed to reach it, so it is neither a refusal nor a failure.
            Searched::Panicked => return self.settle_sync(SyncOutcome::Unreachable),
            // The runtime is going away, and the controller with it.
            Searched::Cancelled => return self.settle_sync(SyncOutcome::Failed(disposed_problem())),
        };
        if self.sending || !self.queue.is_empty() || self.confirmed.as_ref() != Some(&confirmed) {
            return self.pump();
        }
        let next = match answer {
            PlanAnswer::Plan(events) => match events.into_iter().next() {
                Some(event) => event,
                None => {
                    let revision = self.revision;
                    self.settle_sync(SyncOutcome::Synced {
                        snapshot: confirmed,
                        revision,
                    });
                    return;
                }
            },
            // At the target with only the fields event left, which rests on a rule the client cannot evaluate: the
            // server applied it there when it confirmed it, and nothing has been confirmed since.
            PlanAnswer::Undetermined { trigger, events }
                if events.is_empty()
                    && confirmed.state == run.target
                    && run.fields.as_ref().is_some_and(|f| f.trigger == trigger)
                    && run.fields_answered == Some(self.revision) =>
            {
                let revision = self.revision;
                return self.settle_sync(SyncOutcome::Synced {
                    snapshot: confirmed,
                    revision,
                });
            }
            // The events up to the edge the client cannot evaluate, then that edge, unpredicted: the server decides.
            PlanAnswer::Undetermined { trigger, events } => match events.into_iter().next() {
                Some(event) => event,
                None => {
                    let input = run
                        .fields
                        .as_ref()
                        .filter(|f| f.trigger == trigger)
                        .and_then(|f| f.input.clone());
                    PlanEvent::new(trigger, input)
                }
            },
            PlanAnswer::Unreachable => return self.settle_sync(SyncOutcome::Unreachable),
            PlanAnswer::Exceeded => return self.settle_sync(SyncOutcome::Exceeded),
            PlanAnswer::Invalid(invalid) => return self.settle_sync(SyncOutcome::Invalid(invalid)),
        };
        if run.sent >= 2 * self.machine.states().len() {
            return self.settle_sync(SyncOutcome::Exceeded);
        }
        // The sync holds the event as in flight before `pump` runs: a minter that panics answers it there, and the
        // sync has to hear that.
        run.sent += 1;
        run.sending_fields = run.fields.as_ref().is_some_and(|f| f.trigger == next.trigger);
        let entry = self.queued(next.trigger, next.input, None, None);
        if let Some(run) = self.sync.as_mut() {
            run.in_flight = Some(entry);
        }
        self.emit();
        self.pump();
    }

    // A sync's event, or a superseded sync's last one, has answered.
    fn sync_answered(&mut self, entry: u64, outcome: &SendOutcome) {
        if let Some(at) = self.superseded.iter().position(|(e, _)| *e == entry) {
            let (_, reply) = self.superseded.remove(at);
            let _ = reply.send(SyncOutcome::Superseded);
        }
        let Some(run) = self.sync.as_mut().filter(|run| run.in_flight == Some(entry)) else {
            return;
        };
        run.in_flight = None;
        run.fields_answered = None;
        match outcome {
            // The next step plans from the answer once the queue is empty.
            SendOutcome::Confirmed(answer) => {
                if run.sending_fields {
                    run.fields_answered = Some(answer.revision);
                }
            }
            SendOutcome::Refused(problem) if !run.refused && REPLANNED.contains(&problem.code.as_str()) => {
                run.refused = true;
                run.awaiting_read = true;
                // A divergence has started its read already.
                if problem.code != codes::CLIENT_DIVERGENCE {
                    self.reload(None);
                }
            }
            SendOutcome::Refused(problem) => self.settle_sync(SyncOutcome::Refused(problem.clone())),
            SendOutcome::Failed(problem) => self.settle_sync(SyncOutcome::Failed(problem.clone())),
            SendOutcome::Stopped(error) => self.settle_sync(SyncOutcome::Stopped(error.clone())),
        }
    }

    // The reads have ended: a sync that waited for one plans again from it, or settles with what stopped it.
    fn read_settled(&mut self, failure: Option<SyncOutcome>) {
        if self.disposed || self.error.is_some() {
            return;
        }
        let Some(run) = self.sync.as_mut().filter(|run| run.awaiting_read) else {
            return;
        };
        run.awaiting_read = false;
        match failure {
            Some(outcome) => self.settle_sync(outcome),
            None => self.pump(),
        }
    }

    fn settle_sync(&mut self, outcome: SyncOutcome) {
        let Some(run) = self.sync.take() else { return };
        run.stop.store(true, Ordering::Relaxed);
        let unsaved = !matches!(outcome, SyncOutcome::Synced { .. });
        if unsaved != self.unsaved {
            self.unsaved = unsaved;
            self.emit();
        }
        let _ = run.reply.send(outcome);
    }

    fn settle_superseded(&mut self) {
        for (_, reply) in self.superseded.drain(..) {
            let _ = reply.send(SyncOutcome::Superseded);
        }
    }

    // ── Stopping and timing ─────────────────────────────────────────────────────────────────────

    fn stop(&mut self, error: MachineError) {
        if self.error.is_some() || self.disposed {
            return;
        }
        self.error = Some(error.clone());
        self.status = MachineStatus::Stopped;
        self.close();
        // A retry the opening waits for will not run.
        self.epoch += 1;
        self.settle_open();
        self.sending = false;
        let stranded: Vec<Queued> = self.queue.drain(..).collect();
        self.emit();
        for queued in stranded {
            if let Some(reply) = queued.reply {
                let _ = reply.send(SendOutcome::Stopped(error.clone()));
            }
        }
        self.settle_read(SendOutcome::Stopped(error.clone()));
        self.settle_sync(SyncOutcome::Stopped(error));
        self.settle_superseded();
    }

    // Runs `action` after the attempt's delay. The caller checked there is one left.
    fn later(&mut self, attempt: usize, action: Later) {
        let delay = self.retry_delays[attempt];
        let epoch = self.epoch;
        let commands = self.commands.clone();
        self.runtime.spawn(async move {
            tokio::time::sleep(delay).await;
            let _ = commands.send(Msg::Later { epoch, action });
        });
    }

    fn emit(&mut self) {
        if self.disposed {
            return;
        }
        let view = MachineView {
            status: self.status,
            id: self.id.clone(),
            snapshot: self.predicted(),
            confirmed: self.confirmed.clone(),
            row_id: self.row_id.clone(),
            revision: self.revision,
            due_at: self.due_at.clone(),
            pending: !self.queue.is_empty(),
            problem: self.problem.clone(),
            error: self.error.clone(),
            unsaved: self.unsaved,
        };
        self.publish.send_replace(Arc::new(view));
    }

    // The confirmed snapshot with every pending event the machine allows applied in order, up to the first one bound
    // to the effect or resting on a custom rule it cannot evaluate: what the page will show if the server agrees.
    fn predicted(&self) -> Option<Snapshot> {
        let confirmed = self.confirmed.clone()?;
        if self.status == MachineStatus::Stopped {
            return Some(confirmed);
        }
        let mut shown = confirmed;
        for queued in &self.queue {
            if !self.predicts(&shown.state, &queued.trigger) {
                break;
            }
            if let Ok(next) = self.machine.advance(&shown, &queued.trigger, queued.input.as_ref()) {
                shown = next;
            }
        }
        Some(shown)
    }
}

// Whether a snapshot at `incoming` is at or below one at `held`. A missing revision on either side cannot be ordered, so
// it is never older.
fn older_or_same(incoming: Option<i64>, held: Option<i64>) -> bool {
    matches!((incoming, held), (Some(i), Some(h)) if i <= h)
}

/// The context with every null member left out, at every depth: a typed answer cannot tell a member stored as null
/// from one the state does not hold, and the server compares a prediction with null and absent equal. A null in an
/// array is kept: it holds a place. Copies without recursion, so a context of any depth is safe to pass.
pub fn without_nulls(context: &Map) -> Map {
    // One frame per container being copied: what is left to read of it, what has been copied, and, for an object, the
    // key the member being copied below it goes under.
    enum Frame<'a> {
        Object {
            members: std::collections::btree_map::Iter<'a, JsString, Value>,
            copied: Map,
            key: Option<JsString>,
        },
        Array {
            items: std::slice::Iter<'a, Value>,
            copied: Vec<Value>,
        },
    }
    let mut frames = vec![Frame::Object {
        members: context.iter(),
        copied: Map::new(),
        key: None,
    }];
    loop {
        let next = match frames.last_mut() {
            Some(Frame::Object { members, key, .. }) => members.find(|(_, v)| !v.is_null()).map(|(k, v)| {
                *key = Some(k.clone());
                v
            }),
            Some(Frame::Array { items, .. }) => items.next(),
            None => unreachable!("the context's own frame is the last one taken off"),
        };
        let copied = match next {
            Some(Value::Object(map)) => {
                frames.push(Frame::Object {
                    members: map.iter(),
                    copied: Map::new(),
                    key: None,
                });
                continue;
            }
            Some(Value::Array(items)) => {
                frames.push(Frame::Array {
                    items: items.iter(),
                    copied: Vec::new(),
                });
                continue;
            }
            Some(other) => other.clone(),
            None => match frames.pop() {
                Some(Frame::Object { copied, .. }) if frames.is_empty() => return copied,
                Some(Frame::Object { copied, .. }) => Value::Object(copied),
                Some(Frame::Array { copied, .. }) => Value::Array(copied),
                None => unreachable!("a frame was read above"),
            },
        };
        match frames.last_mut() {
            Some(Frame::Object { copied: into, key, .. }) => {
                if let Some(key) = key.take() {
                    into.insert(key, copied);
                }
            }
            Some(Frame::Array { copied: into, .. }) => into.push(copied),
            None => unreachable!("only the context's own frame is returned"),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::StreamSink;

    // The kit's limits.json holds the stored depth every engine and client refuses past.
    #[test]
    fn the_stored_depth_is_the_kits() {
        let file = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/limits.json");
        let limits = Value::parse(&std::fs::read_to_string(&file).unwrap()).unwrap();
        assert_eq!(
            Some(MAX_STORED_DEPTH as f64),
            limits.get("maxStoredDepth").and_then(Value::as_f64)
        );
    }

    struct Reads(Snapshot);

    struct Quiet;

    impl Subscription for Quiet {}

    impl MachineServer for Reads {
        async fn start(&self, _: Option<Value>, _: String) -> Result<ServerAnswer, TransportError> {
            std::future::pending().await
        }

        async fn event(&self, _: EventRequest) -> Result<ServerAnswer, TransportError> {
            std::future::pending().await
        }

        async fn get(&self, _: String) -> Result<ServerAnswer, TransportError> {
            Ok(ServerAnswer::Stored {
                id: None,
                snapshot: ServerSnapshot {
                    row_id: None,
                    revision: Some(1),
                    state: self.0.state.clone(),
                    context: self.0.context.clone(),
                    due_at: DueAt::None,
                },
            })
        }

        fn subscribe(&self, _: String, _: StreamSink) -> Box<dyn Subscription> {
            Box::new(Quiet)
        }
    }

    fn wizard() -> Value {
        let file = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance/plan/wizard.ir.json");
        Value::parse(std::fs::read_to_string(file).unwrap().trim_end_matches('\n')).unwrap()
    }

    // Opens the wizard at Start and asks for a sync, whose search a message sent behind it answers first: on a runtime of
    // one thread the controller's task takes both before the search's own answer can be sent. The search itself would
    // plan Begin, which the server never answers.
    async fn sync_answered_by(searched: Searched) -> SyncOutcome {
        let ir = wizard();
        let hash = ir_hash(&ir);
        let machine = Machine::from_ir(&ir).unwrap();
        let start = machine.initial_snapshot();
        let mut options = ServerMachineOptions::new(ir, hash, Reads(start));
        options.id = Some("i-1".into());
        let controller = ServerMachineController::new(options).unwrap();
        controller.start().await;

        let synced = controller.sync_to("Details", None, vec!["Begin".to_owned()]);
        let _ = controller.commands.send(Msg::Planned { sync: 1, searched });
        synced.await
    }

    #[tokio::test(flavor = "current_thread")]
    async fn a_search_that_panicked_settles_unreachable_not_as_a_refusal() {
        assert_eq!(sync_answered_by(Searched::Panicked).await, SyncOutcome::Unreachable);
    }

    #[tokio::test(flavor = "current_thread")]
    async fn a_search_the_runtime_cancelled_settles_as_disposed() {
        let outcome = sync_answered_by(Searched::Cancelled).await;
        assert!(
            matches!(&outcome, SyncOutcome::Failed(p) if p.code == codes::DISPOSED),
            "{outcome:?}"
        );
    }

    #[tokio::test]
    async fn a_join_error_is_told_apart_as_a_panic_or_a_cancellation() {
        let panicked = tokio::task::spawn_blocking(|| -> Option<(Snapshot, PlanAnswer)> { panic!("a bug") }).await;
        assert!(matches!(Searched::of(panicked), Searched::Panicked));
        let pending = tokio::spawn(std::future::pending::<Option<(Snapshot, PlanAnswer)>>());
        pending.abort();
        assert!(matches!(Searched::of(pending.await), Searched::Cancelled));
    }
}
