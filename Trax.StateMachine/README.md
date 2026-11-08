# Trax.StateMachine

> Part of [Trax .NET](https://github.com/TraxSharp): one description of your app, for the whole team. Every part of the
> app has a place, so you only have to write what it does. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

State machines a page drives and the server applies. A machine is declared once in C#; the server runs it, and a
browser runs the same rules to show what an event will do before the server answers. Each language has a directory
of its own:

| Directory | What it is | Package |
| --- | --- | --- |
| [dotnet](dotnet) | The C# engine, its persistence, its test helpers and its client | `Trax.StateMachine`, `Trax.StateMachine.Persistence`, `Trax.StateMachine.Testing`, `Trax.StateMachine.Client` on NuGet |
| [ts](ts) | The TypeScript engine, and the client a page drives a served machine through | `@traxsharp/state-machine` on npm |
| [rust](rust) | The engine in Rust, which the other languages' bindings are built on | not published yet |
| [conformance](conformance) | The cases every engine must answer the same way, and the runner that sends them | not published |

Docs are at [traxsharp.net/docs/statemachine](https://traxsharp.net/docs/statemachine).

## Contributing

Read [AGENTS.md](AGENTS.md) before changing code. Report vulnerabilities privately as described in
[SECURITY.md](SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.
