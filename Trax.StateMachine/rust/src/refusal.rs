// The refusal codes, one closed set shared by every engine, client and the GraphQL schema
// (conformance/refusal-codes.json). Only the code is contract; the detail is for people.

use std::fmt;

macro_rules! refusal_codes {
    ($($(#[$doc:meta])* $name:ident = $code:literal,)*) => {
        /// Every code a machine operation is refused with, in the order of `conformance/refusal-codes.json`. This
        /// engine reports only some of them; the rest belong to the server, and are here so a client can name them.
        #[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
        pub enum RefusalCode {
            $($(#[$doc])* $name,)*
        }

        impl RefusalCode {
            /// Every code, in the registry's order.
            pub const ALL: &'static [RefusalCode] = &[$(RefusalCode::$name,)*];

            /// The code as it travels: `NO_TRANSITION`.
            pub fn as_str(self) -> &'static str {
                match self {
                    $(RefusalCode::$name => $code,)*
                }
            }

            /// The member name the registry gives the code: `NoTransition`.
            pub fn name(self) -> &'static str {
                match self {
                    $(RefusalCode::$name => stringify!($name),)*
                }
            }

            /// The code spelled `code`, or `None` when no code is.
            pub fn from_code(code: &str) -> Option<RefusalCode> {
                match code {
                    $($code => Some(RefusalCode::$name),)*
                    _ => None,
                }
            }
        }
    };
}

refusal_codes! {
    /// No transition from the state on the trigger, or the state is not one of the machine's.
    NoTransition = "NO_TRANSITION",
    /// A transition exists but its guard rejected the input.
    GuardFailed = "GUARD_FAILED",
    /// A context fails its state's validator.
    InvalidContext = "INVALID_CONTEXT",
    /// An unexpected error was caught inside the engine, such as a custom handler that panicked.
    InternalError = "INTERNAL_ERROR",
    /// The input does not match the trigger's declared schema.
    InvalidInput = "INVALID_INPUT",
    /// The trigger is an invoked train's outcome, which only the outcome path applies.
    OutcomeBound = "OUTCOME_BOUND",
    /// The trigger is a state's timer, which only the server fires.
    TimerBound = "TIMER_BOUND",
    /// The trigger input or the resulting snapshot is past the snapshot size cap.
    TooLarge = "TOO_LARGE",
    /// A refusal of `discard`: the instance is in a state only the server puts it in (a committed state, an effect's
    /// target, a state that invokes a train, a state an invoked train's outcome reaches, or the target of an event
    /// gated by a role or a policy), or an effect on it is running or recording its result.
    DiscardNotAllowed = "DISCARD_NOT_ALLOWED",
    /// An effect is pending on the instance: every other write to it waits until the effect is recorded or has failed.
    /// Retry once it has finished.
    EffectInProgress = "EFFECT_IN_PROGRESS",
    /// Stored JSON was not JSON, not an object, or missing a required field.
    Malformed = "MALFORMED",
    /// The snapshot names a different machine.
    UnknownMachine = "UNKNOWN_MACHINE",
    /// The snapshot was written by a newer definition, or no migration reaches this one.
    VersionMismatch = "VERSION_MISMATCH",
    /// The snapshot's state is not a state of this machine.
    UnknownState = "UNKNOWN_STATE",
    /// The caller does not meet the rule the event, start or read declares.
    Forbidden = "FORBIDDEN",
    /// The request id was already used for a different request.
    RequestIdReused = "REQUEST_ID_REUSED",
    /// The trigger runs the machine's irreversible effect from this state, so only a send fires it, never an advance.
    EffectBound = "EFFECT_BOUND",
    /// A save is not allowed for this machine, because its states are reached through declared events only.
    SaveNotAllowed = "SAVE_NOT_ALLOWED",
    /// The client runs an older version of the machine than the server, and must reload.
    ClientOutdated = "CLIENT_OUTDATED",
    /// The client runs a newer version of the machine than this server; a retry may reach a newer one.
    ServerOutdated = "SERVER_OUTDATED",
    /// The user already holds as many drafts as one user may, so a start or a save creating another is refused.
    TooManyDrafts = "TOO_MANY_DRAFTS",
    /// The request id is missing, empty, whitespace, too long, or holds a character a row cannot store.
    InvalidRequestId = "INVALID_REQUEST_ID",
    /// The caller names no user, so there is no owner to act for.
    Unauthenticated = "UNAUTHENTICATED",
    /// The machine declares no `start`, so a client cannot create an instance of it.
    NoStart = "NO_START",
    /// The instance belongs to the system: only host code creates, drives or deletes it.
    SystemOwned = "SYSTEM_OWNED",
    /// The caller has no instance with this id that it may read: a missing id, another user's, a system instance's, and
    /// one the read rule refuses the caller all get this one answer, so it tells the caller nothing about which.
    NotFound = "NOT_FOUND",
    /// The caller already holds as many open subscriptions as one principal may; one that completes frees its place.
    TooManySubscriptions = "TOO_MANY_SUBSCRIPTIONS",
    /// The client sent a twin's IR hash that differs from the machine this host serves; it must reload.
    SchemaMismatch = "SCHEMA_MISMATCH",
    /// The draft changed elsewhere while this write ran; reload and retry.
    Conflict = "CONFLICT",
    /// The machine declares no irreversible effect, so there is nothing to send.
    NoEffect = "NO_EFFECT",
    /// The effect, or the store around it, failed: the draft was not advanced, so the caller can retry.
    DeliveryFailed = "DELIVERY_FAILED",
    /// The snapshot a client computed differs from the one the server computed for the same transition.
    ClientDivergence = "CLIENT_DIVERGENCE",
    /// The state is completed by its action, not by a save: a client sends the action instead.
    StateReserved = "STATE_RESERVED",
    /// The instance is in a state that invokes a train: it moves on when the train finishes, or through one of its
    /// actions, never through a save.
    DraftInvoking = "DRAFT_INVOKING",
    /// The stored draft cannot be read by this machine definition, so only a start-over overwrites it.
    DraftUnreadable = "DRAFT_UNREADABLE",
    /// The draft was already completed, so an edit may not overwrite it.
    DraftCommitted = "DRAFT_COMMITTED",
    /// The draft's action already ran on content that has changed since; its result is recorded only once that content
    /// is restored.
    DraftChanged = "DRAFT_CHANGED",
    /// The owner already has as many invoked runs live as one owner may, so the step that starts another is refused.
    InvokeLimitReached = "INVOKE_LIMIT_REACHED",
    /// The user entering the state may not run the train it invokes.
    InvokeForbidden = "INVOKE_FORBIDDEN",
    /// An outcome or a timer would enter a state that invokes a train on a user-owned instance: only a system-owned
    /// instance chains runs that way.
    InvokeChainUserOwned = "INVOKE_CHAIN_USER_OWNED",
}

impl fmt::Display for RefusalCode {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

/// A refused operation: the code, which is contract, and a detail for people, which is not.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Refusal {
    /// The code, which is contract.
    pub code: RefusalCode,
    /// What went wrong, for people.
    pub detail: String,
}

impl Refusal {
    pub(crate) fn new(code: RefusalCode, detail: impl Into<String>) -> Refusal {
        Refusal {
            code,
            detail: detail.into(),
        }
    }
}

impl fmt::Display for Refusal {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}: {}", self.code, self.detail)
    }
}

impl std::error::Error for Refusal {}
