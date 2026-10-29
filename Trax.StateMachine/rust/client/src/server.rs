//! The seam between the controller and a host: the operations of one served machine and its change stream.

use std::fmt;
use std::future::Future;

use trax_state_machine::{Map, Value};

/// One snapshot of an instance as the server stored it, with the values that travel beside it rather than in its
/// canonical wire.
#[derive(Clone, Debug, PartialEq)]
pub struct ServerSnapshot {
    /// The row's incarnation: deleting the instance and creating it again gives a new one, which restarts the
    /// revision. `None` when the answer does not carry it (an event's or a read's answer).
    pub row_id: Option<String>,
    /// Bumped by the database on every write of the instance. `None` when the store records none: the snapshot cannot
    /// be ordered, so it is never dropped as stale, and whatever comes after it is taken.
    pub revision: Option<i64>,
    /// The state the instance is in.
    pub state: String,
    /// The state's context, with no member whose value is null.
    pub context: Map,
    /// When the state's timer is due.
    pub due_at: DueAt,
}

/// What an answer says about the state's timer.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum DueAt {
    /// The answer does not know (it does not carry the due time, or replays a request the instance has moved past).
    Unknown,
    /// The state has no timer, or the server cleared it.
    None,
    /// When the timer is due, as the server wrote it (ISO-8601).
    At(String),
}

/// A refusal, as a value. `code` is the contract (`GUARD_FAILED`); the message is for a person.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ServerProblem {
    pub code: String,
    pub message: String,
}

/// What the server answered a start, an event or a read with.
#[derive(Clone, Debug, PartialEq)]
pub enum ServerAnswer {
    /// What the server stored, and the instance's id on a start's answer.
    Stored {
        id: Option<String>,
        snapshot: ServerSnapshot,
    },
    /// The server refused.
    Refused(ServerProblem),
}

/// A request that did not get through, or whose answer did not come back. The controller reports it as
/// `TRANSPORT_ERROR`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TransportError(pub String);

impl fmt::Display for TransportError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for TransportError {}

/// What an instance's change stream delivers.
#[derive(Clone, Debug, PartialEq)]
pub enum StreamMessage {
    /// The instance as it is now. The first message of every new stream is one.
    Changed(ServerSnapshot),
    /// The instance is gone. `not_found` when the server answered `NOT_FOUND`, which it answers alike for an
    /// instance that is missing and one that is not the caller's; false when it was deleted.
    Gone { not_found: bool },
    /// The server asks for the instance to be read again: anything may have been missed.
    Reload,
    /// The server no longer serves this client's version of the machine: a push, or a `next` or `error` coded
    /// `CLIENT_OUTDATED`.
    ClientOutdated,
    /// The host refused the socket or the stream: it closed the socket with 4401 or 4403, answered its upgrade 401 or
    /// 403, or answered the stream with an error whose code is `AUTH_NOT_AUTHENTICATED`, `TRAX_AUTHORIZATION` or
    /// `AUTH_NOT_AUTHORIZED`. Subscribing again would get the same answer. The code is `UNAUTHENTICATED` for 4401, 401
    /// and `AUTH_NOT_AUTHENTICATED`, `FORBIDDEN` for the rest; a stream error whose code the caller marked terminal
    /// keeps its code.
    Refused(ServerProblem),
}

/// One event, as the controller sends it.
#[derive(Clone, Debug, PartialEq)]
pub struct EventRequest {
    pub id: String,
    pub trigger: String,
    pub input: Option<Value>,
    pub request_id: String,
    /// The canonical wire of the snapshot the client predicted, which the server compares with its own.
    pub client_result: Option<String>,
}

/// Where a stream delivers its messages. Cloned into whatever task reads the stream.
pub type StreamSink = std::sync::Arc<dyn Fn(StreamMessage) + Send + Sync>;

/// A stream the controller listens to. Dropping it closes the stream.
pub trait Subscription: Send {}

/// The operations of one machine a host serves, and its change stream: what a
/// [`ServerMachineController`](crate::ServerMachineController) drives one instance through. A refusal is
/// `Ok(ServerAnswer::Refused)`; a request that did not get through is `Err`, and so is one that panics (under
/// `panic = "unwind"`). [`GraphQLMachineServer`](crate::GraphQLMachineServer) is the one over a host's typed `machines`
/// root.
pub trait MachineServer: Send + Sync + 'static {
    /// Creates an instance from the machine's start. A retry under the same request id answers the same instance.
    fn start(
        &self,
        input: Option<Value>,
        request_id: String,
    ) -> impl Future<Output = Result<ServerAnswer, TransportError>> + Send;

    /// Fires one event.
    fn event(&self, request: EventRequest) -> impl Future<Output = Result<ServerAnswer, TransportError>> + Send;

    /// Reads the instance; refused with `NOT_FOUND` when there is none the caller may read.
    fn get(&self, id: String) -> impl Future<Output = Result<ServerAnswer, TransportError>> + Send;

    /// Listens to the instance's changes. A stream that ends without saying why is subscribed again by the server,
    /// so `sink` hears only of what the controller must act on.
    fn subscribe(&self, id: String, sink: StreamSink) -> Box<dyn Subscription>;
}
