-- State-machine draft persistence (Trax.Effect.StateMachine.Persistence). The engine, EF store, and
-- exactly-once effect ledger live in the StateMachine.Persistence package; these are the two tables its
-- SnapshotDbContext maps. They live in the `trax` schema like every other Trax table, and — as with
-- 035_persisted_operations — a higher-level feature's tables ship in this migration set so the standard
-- DatabaseMigrator creates them automatically at startup (a host that uses AddTraxStateMachines needs no
-- extra table wiring; a host that doesn't just carries two empty tables).
--
-- Columns mirror SnapshotRecord / EffectClaim (Trax.Effect.StateMachine.Persistence/Entities.cs) EXACTLY —
-- the EfSnapshotStore / EffectClaimStore query these by their [Column(...)] names, so the DDL must match.

-- One persisted draft, scoped to a user. `id` is CLIENT-minted (a Guid) and unique only per user, so the
-- primary key is composite (user_key, id) — its leading column also serves the user-scoped reads.
-- `context` is a real jsonb column; `concurrency_token` is an app-managed optimistic-concurrency token
-- (a fresh Guid on every write, CAS'd in the atomic Update path — provider-agnostic, unlike xmin).
create table if not exists trax.snapshot_draft
(
    id                uuid        not null,
    user_key          text        not null,
    machine           text        not null,
    version           integer     not null,
    state             text        not null,
    context           jsonb       not null default '{}',
    concurrency_token uuid        not null,
    last_request_id   text        null,
    updated_at        timestamptz not null,
    constraint pk_snapshot_draft primary key (user_key, id)
);

-- Generic idempotency ledger for exactly-once SIDE EFFECTS (send a letter, charge a card). A transition
-- bound with .RunsOnce<TEffect>() claims its `effect_key` here BEFORE running the effect; the unique key is
-- the lock. `receipt` NULL = claimed but in flight; `owner_token` is the fence token (Complete / ReleaseOwned
-- CAS on it) and `lease_expires_at` bounds an abandoned claim so the next caller (or the sweeper) can reclaim
-- it. Machine-agnostic — the `effect_key` names the INTENT, never the content.
create table if not exists trax.effect_claim
(
    effect_key       text        not null,
    receipt          text        null,
    owner_token      uuid        not null,
    lease_expires_at timestamptz not null,
    created_at       timestamptz not null,
    constraint pk_effect_claim primary key (effect_key)
);
