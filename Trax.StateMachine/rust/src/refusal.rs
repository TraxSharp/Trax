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
    TooLarge = "TOO_LARGE",
    DiscardNotAllowed = "DISCARD_NOT_ALLOWED",
    EffectInProgress = "EFFECT_IN_PROGRESS",
    /// Stored JSON was not JSON, not an object, or missing a required field.
    Malformed = "MALFORMED",
    /// The snapshot names a different machine.
    UnknownMachine = "UNKNOWN_MACHINE",
    /// The snapshot was written by a newer definition, or no migration reaches this one.
    VersionMismatch = "VERSION_MISMATCH",
    /// The snapshot's state is not a state of this machine.
    UnknownState = "UNKNOWN_STATE",
    Forbidden = "FORBIDDEN",
    RequestIdReused = "REQUEST_ID_REUSED",
    EffectBound = "EFFECT_BOUND",
    SaveNotAllowed = "SAVE_NOT_ALLOWED",
    ClientOutdated = "CLIENT_OUTDATED",
    ServerOutdated = "SERVER_OUTDATED",
    TooManyDrafts = "TOO_MANY_DRAFTS",
    InvalidRequestId = "INVALID_REQUEST_ID",
    Unauthenticated = "UNAUTHENTICATED",
    NoStart = "NO_START",
    SystemOwned = "SYSTEM_OWNED",
    NotFound = "NOT_FOUND",
    TooManySubscriptions = "TOO_MANY_SUBSCRIPTIONS",
    SchemaMismatch = "SCHEMA_MISMATCH",
    Conflict = "CONFLICT",
    NoEffect = "NO_EFFECT",
    DeliveryFailed = "DELIVERY_FAILED",
    ClientDivergence = "CLIENT_DIVERGENCE",
    StateReserved = "STATE_RESERVED",
    DraftInvoking = "DRAFT_INVOKING",
    DraftUnreadable = "DRAFT_UNREADABLE",
    DraftCommitted = "DRAFT_COMMITTED",
    DraftChanged = "DRAFT_CHANGED",
    InvokeLimitReached = "INVOKE_LIMIT_REACHED",
    InvokeForbidden = "INVOKE_FORBIDDEN",
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
    pub code: RefusalCode,
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
