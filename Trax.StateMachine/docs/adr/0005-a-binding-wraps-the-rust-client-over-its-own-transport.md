---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A binding wraps the Rust client, and brings only its platform's transport

Swift, Kotlin, Python and every language after them get the machine client by binding the Rust one
(`rust/client`), not by writing their own. Each binding implements a small transport the Rust controller calls
(an HTTP request, a socket opened, a frame sent and received, a close), on its platform's own stack, and marshals the
view and the outcomes. TypeScript and C# keep their own clients, because each has its own engine and runs where the
Rust core does not (a browser without WebAssembly, React Native's Hermes, the .NET server).

## Status

**Accepted.**

## Considered options

**A native client per language**, each written to its platform from the client kit. Rejected: the controller is
where the clients went wrong. Across three audit rounds of the TypeScript, C# and Rust clients, most findings were
in what every client does the same way and none of the three wrote the same: the order of a queued send against a
divergence or a reload, a callback that throws, a malformed push, dispose with a request in flight, backoff and its
reset. A fourth and fifth copy of about 4,600 lines (`controller.rs` and `graphql.rs`) would repeat them, and the kit
only catches what it already has a transcript for.

**Binding the Rust client whole, its transport included.** Rejected: it brings its own runtime and TLS stack, so on
iOS it would skip URLSession, App Transport Security, system proxies and the platform's certificate store, and in
Python it would sit beside the application's event loop rather than in it.

## Consequences

The Rust client's controller has to be callable across a binding: no generic `MachineServer` at the boundary, no
`impl Future` in a trait a foreign language implements, JSON text where a type would need a layout, and a panic
caught before it crosses. That shape is work the first binding pays for.

A binding's own code is marshalling and a transport. The transport is where the platform's defaults bite (redirects
followed with auth headers, `ws://` allowed, a 1 MiB socket message cap), and the client kit has a transcript for
each.

The controller's behaviour is fixed once for every bound language; a fix to it reaches all of them in one change, and
a fix to the TypeScript or C# client still has to be made again in the other two.

## Exemplars

**Unenforced:** the kit judges a client by its answers, and a native client that passes every transcript answers
the same as a bound one, so no test can tell which a language wrote. Review holds it, through step 4 of "Adding a
language" in this folder's `AGENTS.md`.

Not covered: the behaviour itself, which the client kit holds every client to whichever way it was built.

## Changelog

- **2026-10-31**: Recorded, before the Swift and Python bindings.
