# trax-state-machine

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

The state-machine engine in Rust. A machine is declared once in C# and exported as an IR; this crate builds the
machine from that IR and answers what the server would do with a trigger, byte for byte on the wire and code for
code on a refusal. It is pure (no I/O, no clock, no async) and has no dependencies, so the bindings for other
languages can be built on it. Every operation is total: a refusal is a value, never a panic. A custom handler
that panics is caught and refused with `INTERNAL_ERROR`, which needs the build that links the crate to unwind on
panic (`panic = "unwind"`, Rust's default). Under `panic = "abort"` such a panic ends the process. A `Value` built in
memory that nests deeper than `MAX_DEPTH` (512) is refused as its text would be, `INVALID_INPUT` for an input and
`INVALID_CONTEXT` for a context, and is written and dropped without recursion; check `Value::is_too_deep` before
cloning or comparing one a binding built.

## Install

Not published yet. Depend on it by path or by git:

```toml
[dependencies]
trax-state-machine = { git = "https://github.com/TraxSharp/Trax" }
```

## Example

```rust
use trax_state_machine::{Machine, Value};

let machine = Machine::from_ir_text(&std::fs::read_to_string("checkout.ir.json")?)?;
let draft = machine.rehydrate(&stored)?;
match machine.advance(&draft, "Pay", Some(&Value::parse(r#"{"receipt":null}"#)?)) {
    Ok(next) => save(machine.serialize(&next)),
    Err(refusal) => show(refusal.code.as_str()),
}
```

`trax-state-machine-client` (`client/`) is the client over it: `ServerMachineController` drives one instance a host
serves, predicting each event with the machine built from the manifest's IR, over `GraphQLMachineServer` (persisted
ids over HTTP, the change stream over graphql-transport-ws, on tokio). An event with an edge that rests on a custom
rule is sent with no prediction, since the client binds no handler. A stream that ends without saying why is
subscribed again after `resubscribe_delays`; a socket or a stream the host refused (closed with 4401 or 4403, an
upgrade answered 401 or 403, or a stream error coded `AUTH_NOT_AUTHENTICATED`, `TRAX_AUTHORIZATION` or
`AUTH_NOT_AUTHORIZED`) is not, and stops the controller as `UNAUTHENTICATED` or `FORBIDDEN`, as does a stream error with
one of the caller's `terminal_stream_errors`, with its own code. The controller's task runs
on the tokio runtime `ServerMachineOptions::runtime` names, or the one it is built in, and may be called from any
thread once built. It is held to the client conformance kit, as the TypeScript and C# clients are.

`advance` takes the triggers a client sends, `apply_outcome` an invoked train's outcome and `apply_timer` a state's
timer. A custom rule is bound by name with `with_guard` or `with_reducer`; one with no handler fails, so the engine
leaves that step to the server.

## Where this fits

- [The IR format](https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-format) this crate reads.
- [The canonical wire](https://traxsharp.net/docs/statemachine/canonical-wire) it writes.
- `../conformance/`, the kit it is held to, beside the C# and TypeScript engines.

## Contributing

Read [../AGENTS.md](../AGENTS.md) first. Nothing runs on the host here; the tests and the kit run in containers:

```bash
podman run --rm -v "$PWD/..":/src:Z -w /src/rust docker.io/library/rust:1.88.0-bookworm \
  sh -c 'rustup component add rustfmt clippy && cargo fmt --all --check && cargo clippy --workspace --all-targets -- -D warnings && cargo test --workspace && cargo build --release --workspace'
podman run --rm -v "$PWD/..":/src:Z -w /src docker.io/library/node:24-bookworm \
  sh -c 'node conformance/run.mjs --testee /src/rust/target/release/trax-state-machine-testee && node conformance/client/run.mjs --testee /src/rust/target/release/trax-state-machine-client-testee'
```

Run them from this directory. The testee speaks `../conformance/PROTOCOL.md` through the crate's public API only.

## License

MIT. There is no commercial edition, and there will not be one.
