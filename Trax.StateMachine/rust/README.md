# trax-state-machine

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

The state-machine engine in Rust. A machine is declared once in C# and exported as an IR; this crate builds the
machine from that IR and answers what the server would do with a trigger, byte for byte on the wire and code for
code on a refusal. It is pure (no I/O, no clock, no async) and has no dependencies, so the bindings for other
languages can be built on it. Every operation is total: a refusal is a value, never a panic.

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
  sh -c 'rustup component add rustfmt clippy && cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test && cargo build --release'
podman run --rm -v "$PWD/..":/src:Z -w /src docker.io/library/node:24-bookworm \
  node conformance/run.mjs --testee /src/rust/target/release/trax-state-machine-testee
```

Run them from this directory. The testee speaks `../conformance/PROTOCOL.md` through the crate's public API only.

## License

MIT. There is no commercial edition, and there will not be one.
