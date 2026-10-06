---
authors: [Theauxm]
areas: [scheduling, platform, providers]
status: accepted
---

# A runner shares its accepted nonces through the database

A signing runner ([0006](./0006-a-runner-requires-an-authorization-posture.md)) accepts a
synchronous request's nonce once. It now keeps those nonces in an `INonceStore`, and by default
that store is the `runner_nonce` table in the Trax database, so every instance of a runner that
shares the database accepts a request once between them. Memory is kept for a runner that runs as
one instance, and only when the host asks for it.

## Status

**Accepted.** Replaces the per-process nonce memory [0006](./0006-a-runner-requires-an-authorization-posture.md)
left as a known limit.

## The decision

- **The database is the default.** `AddTraxJobRunner` gives a runner with a `SigningKey` the
  database store when the host has a relational data provider (`UsePostgres`, `UseSqlite`). An
  insert records the nonce, so two instances racing one request are serialized by the primary key
  and one of them is refused. When the key refuses the insert, a row past its expiry is taken over
  by one guarded update, and a live one means a replay. The store deletes expired rows every 256
  recorded nonces.
- **Memory is opt-in.** `UseInMemoryNonceStore()` keeps the nonces in the process, which is
  correct only for a single instance.
- **No silent fallback.** A signing runner with no relational provider, no registered store and no
  opt-in refuses to start, naming the three ways out. Falling back to memory quietly would restore
  the per-instance limit on exactly the hosts that did not think about it.
- **A host can bring its own.** A singleton `INonceStore` registered before or after
  `AddTraxJobRunner` replaces the default, for a host that would rather share nonces through a
  cache it already runs.

Only a synchronous request consults the store. SQS and an asynchronous Lambda `Execute` are still
checked for the MAC only, for the reason in 0006: those transports redeliver by design.

## Where the table lives

In Trax.Effect, with its model: the migrations (Postgres `047_runner_nonce.sql`, Sqlite
`012_runner_nonce.sql`), the `RunnerNonce` model, its persistent mapping and
`IDataContext.RunnerNonces`, not in this repo. The Scheduler owns no schema: central `docs/0009`
says a feature package's table ships with the core provider migrations or it never runs, and central
`docs/0036` says it ships with its model, which the feature reaches through the data context.

The store therefore holds no SQL. It adds a `RunnerNonce` and saves; when the save fails,
`ISqlDialect.IsUniqueViolation` decides whether the primary key refused it, which is the only failure
read as a replay; then an `ExecuteUpdate` guarded on `expires_at` takes an expired row over, and an
`ExecuteDelete` sweeps. Both are EF over the model, identical on both providers, and the one thing
that differs between them, how a key conflict is reported, lives in the dialect
([0002](./0002-a-database-provider-is-interchangeable.md)). Insert first, rather than update first,
because a fresh nonce is the common case and costs one statement; the conflict path runs only for a
replay or an expired nonce, which is rare.

The cost is release order. A Scheduler carrying this needs the Effect release that carries the
table, the model and `IsUniqueViolation`, and its pin, before it ships.

## Alternatives

**Keep the memory per process** (0006 as recorded). The window was bounded by the clock skew, but a
runner behind a load balancer is the normal deployment for the HTTP topology, and there each instance
kept its own record of what it had accepted.

**Memory by default, the database opt-in.** No write on the synchronous path unless asked for. It
leaves every scaled runner with the weaker check until someone reads this, which is the wrong way
round for Trax.

**A distributed cache (Redis and the like).** Another dependency for every runner host, when each
already has the database for its metadata rows. `INonceStore` leaves the door open for a host that
has one.

## Consequences

**Every synchronous run writes a row** before the train starts, and a delete runs every 256 of
them. Both touch one small table by its primary key or its `expires_at` index.

**A runner without a database that signs requests must now choose.** A Lambda `Run` function with no
data provider, for example, adds `UseInMemoryNonceStore()` or registers a store.

## Exemplars

- `SharedNonceStoreTests` runs two runner instances on one Postgres database: a request accepted by
  one is refused by the other, eight concurrent verifications of one request accept it once, an
  expired row is taken over, and expired rows are deleted.
- `SqliteSharedNonceStoreTests` runs the same store against Sqlite, including a race of two
  instances, the sweep, and an insert refused by something other than the key (a trigger shares
  Sqlite's constraint code) throwing rather than reading as a replay.
- `NonceStoreSelectionTests` pins the selection: the refusal to start without a store, the opt-in,
  a host's own store in either registration order, and no store needed without a key.
- `InMemoryNonceStoreTests` covers the opt-in store: once per nonce, expiry, and one winner under
  concurrency.
- [Remote Execution](/docs/scheduler/remote-execution) is the rule this produces.

**Enforced elsewhere:** Trax.Effect's `EveryTableIsModelledTests` and `SqliteEveryTableIsModelledTests`
pin `runner_nonce` to its model column for column, and `RunnerNonceStorageTests` with
`SqliteRunnerNonceStorageTests` cover the round trip, the expiry comparisons and what
`IsUniqueViolation` does and does not recognise.

Not covered: nothing checks that a host running several instances did not choose
`UseInMemoryNonceStore()`; that is the host's statement about its own deployment.

## Changelog

- **2026-09-28**: The store uses the `RunnerNonce` model through `IDataContext` and
  `ISqlDialect.IsUniqueViolation` instead of raw `INSERT ... ON CONFLICT` with the schema read
  from `metadata`'s mapping (central `docs/0036`).
- **2026-09-27**: Recorded, with the change it describes.
