-- Persisted GraphQL operations: server-managed manifest mapping operation IDs to documents.
-- Mobile clients (and any other shipped consumer) ship a build-time-stable id
-- (e.g. "userProfile.v1"). The server resolves the id to the current document text,
-- which can be hot-edited without a client redeploy as long as the response shape
-- stays compatible. The shape_fingerprint column captures the structural hash that
-- the application-layer guardrail compares against on edits.

create table if not exists trax.persisted_operation
(
    -- Tenant scope. Empty string ('') is the sentinel for "no tenant" / single-tenant
    -- deployments; a real tenant id is any non-empty string. The column is NOT NULL
    -- because it participates in the primary key (Postgres disallows NULLs in PK columns).
    -- The storage layer normalizes string? at the C# boundary: null is mapped to ''
    -- on write and back to null on read, so callers see clean nullable semantics.
    -- Kept on the primary key to allow per-tenant manifests in a future release without
    -- requiring a schema migration.
    tenant_key varchar not null default '',

    -- Stable build-time id. Convention is "<operationName>.v<N>" (e.g. "userProfile.v1");
    -- developers bump the version manually when a breaking shape change is required.
    id varchar not null,

    -- Original GraphQL operation name. Stored separately so dashboards / CLI can group
    -- by name regardless of version.
    operation_name varchar not null,

    -- Numeric version extracted from the id suffix. Convenience column for ordering.
    version integer not null,

    -- The GraphQL document text that the id resolves to.
    document text not null,

    -- Canonicalized structural hash of the response shape (sha-256 hex).
    -- The shape-diff guardrail compares old vs new on edits and rejects changes
    -- that would break shipped clients (unless explicitly forced).
    shape_fingerprint varchar not null,

    -- Soft-delete flag. Deactivated rows are not served; clients sending the id
    -- get a typed error so the consumer app can prompt the user to update.
    is_active boolean not null default true,

    -- Required reason when deactivating. Surfaces in audit history.
    deprecation_reason varchar,

    -- Optional human-readable description (operator-facing).
    description varchar,

    -- Audit timestamps.
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),

    -- Composite PK: scoped by tenant so tenant A's "userProfile.v1" and tenant B's
    -- "userProfile.v1" do not collide. NULL tenant_key participates in the PK.
    constraint persisted_operation_pkey primary key (tenant_key, id)
);

-- Partial index on active rows: the request path filters by is_active = true,
-- and the inactive set is small relative to active.
create index if not exists persisted_operation_active_idx
    on trax.persisted_operation (tenant_key, id)
    where is_active = true;

-- Index for dashboards/CLI grouping by operation name within a tenant.
create index if not exists persisted_operation_name_idx
    on trax.persisted_operation (tenant_key, operation_name);


-- History table: every upsert / deactivate / restore appends a row here so the
-- dashboard can show "what changed and when" and roll back to a prior document.
-- The active row in trax.persisted_operation is always the latest; history is
-- for audit and rollback only, never read on the request path.
create table if not exists trax.persisted_operation_history
(
    -- Auto-incrementing surrogate. bigserial because edit history can grow large
    -- in a long-lived deployment.
    history_id bigserial primary key,

    -- Mirrors the (tenant_key, id) of the operation row. Same sentinel semantics
    -- as trax.persisted_operation.tenant_key: '' means "no tenant".
    tenant_key varchar not null default '',
    id varchar not null,

    -- Snapshot of the document text at the time of the change.
    document text not null,

    -- Snapshot of the shape fingerprint at the time of the change.
    shape_fingerprint varchar not null,

    -- The kind of change: "upsert", "deactivate", "restore".
    change_type varchar not null,

    -- When the change happened.
    changed_at timestamptz not null default now(),

    -- Required on deactivate, optional on upsert/restore.
    changed_reason varchar
);

-- Lookup index for "show me the history of this operation in reverse-chronological order".
create index if not exists persisted_operation_history_id_idx
    on trax.persisted_operation_history (tenant_key, id, changed_at desc);
