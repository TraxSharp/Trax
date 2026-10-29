//! Drive a Trax state machine a host serves, from Rust: the third native client beside the TypeScript and C# ones,
//! held to the same transcripts by the client conformance kit (`Trax.StateMachine/conformance/client/`).
//!
//! [`ServerMachineController`] drives one instance. The server applies every transition; the controller predicts each
//! event with the machine built from the IR in the host's client manifest (the Rust core), shows the prediction while
//! the event is pending and the server's answer when it comes. [`GraphQLMachineServer`] sends each operation by the
//! persisted id the manifest lists and follows the instance over graphql-transport-ws. Every operation is total: a
//! refusal or a request that did not get through is a value.

mod controller;
mod graphql;
mod server;

pub use controller::{
    codes, ir_hash, without_nulls, MachineError, MachineProblem, MachineStatus, MachineView, ProblemKind, SendOutcome,
    ServerMachineController, ServerMachineOptions, DEFAULT_RETRY_DELAYS,
};
pub use graphql::{
    event_field, machine_field, ConfigError, ConnectionParams, GraphQLMachineServer, GraphQLMachineServerOptions,
    ManifestDocument, Provided, RequestHeaders, DEFAULT_CONNECT_TIMEOUT, DEFAULT_IDLE_TIMEOUT,
    DEFAULT_MAX_MESSAGE_SIZE, DEFAULT_RESUBSCRIBE_DELAYS,
};
pub use server::{
    DueAt, EventRequest, MachineServer, ServerAnswer, ServerProblem, ServerSnapshot, StreamMessage, StreamSink,
    Subscription, TransportError,
};
