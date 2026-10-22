//! The state-machine engine of Trax, in Rust: a machine built from the IR the C# engine exports, over snapshots.
//!
//! Pure: no I/O, no clock, no async. Every operation is total, so a refusal is a value with a code from the shared
//! registry, never a panic. The wire is canonical JSON (RFC 8785), byte for byte what the C# and TypeScript engines
//! write, and the conformance kit in `Trax.StateMachine/conformance/` holds the three to the same answers.
//!
//! ```
//! use trax_state_machine::{Machine, Value};
//!
//! let ir = r#"{"id":"turnstile","version":1,"initialState":"Locked","initialContext":{},
//!   "states":["Locked","Unlocked"],"triggers":["Coin","Push"],"committedStates":[],
//!   "context":{"Locked":{"fields":[]},"Unlocked":{"fields":[]}},"inputs":{},
//!   "transitions":[{"from":"Locked","trigger":"Coin","to":"Unlocked"},
//!                  {"from":"Unlocked","trigger":"Push","to":"Locked"}]}"#;
//! let machine = Machine::from_ir_text(ir).unwrap();
//! let next = machine.advance(&machine.initial_snapshot(), "Coin", None).unwrap();
//! assert_eq!(
//!     machine.serialize(&next),
//!     r#"{"machine":"turnstile","version":1,"state":"Unlocked","context":{}}"#
//! );
//! assert_eq!(machine.advance(&next, "Coin", None).unwrap_err().code.as_str(), "NO_TRANSITION");
//! ```

mod formats;
mod ir;
mod machine;
mod refusal;
mod rules;
mod stored;
mod value;

pub use formats::duration_ticks;
pub use ir::{IrError, IR_FORMAT_VERSION, OLDEST_IR_FORMAT_READ};
pub use machine::{canonical_wire, GuardFn, Machine, MigrationFn, ReducerFn, Snapshot, Timer, TimerField};
pub use refusal::{Refusal, RefusalCode};
pub use value::{number_text, JsString, Map, ParseError, Value, MAX_DEPTH};
