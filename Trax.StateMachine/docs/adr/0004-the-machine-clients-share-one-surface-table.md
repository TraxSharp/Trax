---
authors: [Theauxm]
areas: [platform, testing]
status: accepted
---

# The machine clients share one surface, written down once, and each language is held to it

The TypeScript, .NET and Rust machine clients are one client in three languages. Their behaviour is held together by
the client kit; their public surface is held together by one table, "The client surface" in
`conformance/client/PROTOCOL.md`, which names each operation, option and view member in all three languages, or says
with a reason why a language has none. A meta test in each language fails when a public member of a type the table
covers has no row, or a row names a member that does not exist. A member added to one client is therefore a row the
same pull request writes for all three.

## Status

**Accepted.**

## Why this is written down

Because it is a cost every client change pays, and it is not obvious from any one language why a .NET change has to
touch a Markdown table that a Rust test reads.

## Considered options

**Each language's own public API baseline only** (the .NET `PublicApi` snapshots, the TypeScript exports baseline).
Rejected as enough on its own: each catches a change in its language, and none notices that the other two did not
make it. The three clients drifted exactly that way before the table: options present in one and missing in another,
the same default under different names.

**A capability list the kit derives from the testees.** Rejected: the testees drive the clients through the kit's ops,
so a list derived from them covers what the kit drives and nothing a page calls besides (`can`, options, defaults).

**Generating two clients from the third.** Rejected: each is written to its platform's idiom (a React hook, a .NET
`Task`, a tokio future), which a generator would flatten, and the Rust client is what the coming bindings wrap.

## Consequences

Names may differ by language convention (`syncTo`, `SyncToAsync`, `sync_to`); the table records each, so a
mismatch in meaning shows as a row, not a guess. A cell that names no member must carry its reason, so "not yet" is visible.
Outcome and problem codes are not in the table: the transcripts compare them by value.

## Exemplars

- `ClientSurfaceTests` holds the .NET clients to the table, both ways, and refuses a malformed table.
- `ts/src/clientSurface.test.ts` and `rust/client/tests/client_surface.rs` hold the TypeScript and Rust clients to
  the same table.

Not covered: a member of a type the table does not list as covered; a new public type has to be added to the covered
types by hand.

## Changelog

- **2026-10-29**: Recorded.
