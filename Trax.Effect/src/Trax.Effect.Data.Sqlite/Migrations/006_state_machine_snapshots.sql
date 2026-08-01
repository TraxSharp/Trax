-- SQLite parity for Postgres 040_state_machine_snapshots.sql. The state-machine draft store and
-- exactly-once effect ledger (Trax.Effect.StateMachine.Persistence) ship their tables in the core
-- provider migration set so DatabaseMigrator creates them automatically at startup — a host that calls
-- AddTraxStateMachines needs no extra table wiring, a host that doesn't just carries two empty tables.
--
-- SQLite has no schemas and no jsonb/uuid/timestamptz types, so the tables are unqualified and every
-- column is TEXT/INTEGER (SnapshotDbContext strips the "trax" schema and maps jsonb -> TEXT when it runs
-- on SQLite, so these names/types are exactly what EfSnapshotStore / EffectClaimStore query).

-- One persisted draft, scoped to a user. `id` is CLIENT-minted (a Guid) and unique only per user, so the
-- primary key is composite (user_key, id). `concurrency_token` is an app-managed optimistic-concurrency
-- token (a fresh Guid on every write, CAS'd in the atomic Update path).
CREATE TABLE IF NOT EXISTS snapshot_draft (
    id                TEXT NOT NULL,
    user_key          TEXT NOT NULL,
    machine           TEXT NOT NULL,
    version           INTEGER NOT NULL,
    state             TEXT NOT NULL,
    context           TEXT NOT NULL DEFAULT '{}',
    concurrency_token TEXT NOT NULL,
    last_request_id   TEXT,
    updated_at        TEXT NOT NULL,
    CONSTRAINT pk_snapshot_draft PRIMARY KEY (user_key, id)
);

-- Generic idempotency ledger for exactly-once side effects. A transition bound with .RunsOnce<TEffect>()
-- claims its `effect_key` here BEFORE running the effect; the unique key is the lock. `receipt` NULL =
-- claimed but in flight; `owner_token` is the fence token (Complete / ReleaseOwned CAS on it) and
-- `lease_expires_at` bounds an abandoned claim so the next caller (or the sweeper) can reclaim it.
CREATE TABLE IF NOT EXISTS effect_claim (
    effect_key       TEXT NOT NULL,
    receipt          TEXT,
    owner_token      TEXT NOT NULL,
    lease_expires_at TEXT NOT NULL,
    created_at       TEXT NOT NULL,
    CONSTRAINT pk_effect_claim PRIMARY KEY (effect_key)
);
