-- Gives a state-machine draft an owner kind, and the columns a machine that invokes trains needs
-- (Trax.Effect.StateMachine.Persistence). Columns mirror SnapshotDraft exactly.
--
-- owner_kind: 'user' or 'system'. A user row names its user in user_key; a system row, created only
-- from code by IMachineInstances.Start, has no user key at all. A reserved key string could not mark
-- system rows, because each host maps principals to keys in its own ISnapshotPrincipal and any host's
-- mapping could produce it. Every existing row is a user's, so the column defaults to 'user'.
--
-- invoke_token: the external id of the train run the current state invoked, set when an invoking state
-- is entered and cleared when it is left. An outcome is applied with UPDATE ... WHERE invoke_token =
-- the run's, so it is unique where set. It is server-only and never part of the snapshot.
--
-- The key: user_key becomes nullable, and a key column cannot be, so the primary key moves to a
-- surrogate row_id. Identity is two partial unique indexes: (user_key, machine, id) among user rows,
-- which is the key 051 gave every row, and (machine, id) among system rows. The user index is built
-- before the old key is dropped, so a host still running the previous version never writes a duplicate
-- in between. That host keeps working: it inserts without owner_kind or row_id (both default), and its
-- reads name a user_key, which a system row never matches.
--
-- (machine, state) indexes the operator listing of instances by state.
--
-- Drafts are saved and advanced all the time and the table may hold millions of rows (071 assumes as
-- much), so nothing here rewrites it or holds it locked for a scan (effect/0014):
--   * Every column is added without a default the table would have to be rewritten for: owner_kind's
--     constant default is kept in the catalog, and row_id is added empty, given its sequence as a
--     default for rows written from then on, and filled for the existing rows in batches, each
--     committed on its own.
--   * row_id is made NOT NULL through a check added NOT VALID and validated under a lock that lets
--     writes continue, which SET NOT NULL then trusts instead of scanning; the check is dropped after.
--   * The key's index and the identity indexes are built CONCURRENTLY. The new key takes the name
--     pk_snapshot_draft, which 051 gave the old key, so the old key is renamed out of its way first.
--     The swap from the old key to the new one is then one short transaction that reads no rows.
--   * The owner check is added NOT VALID and validated the same way.
-- A script that stops partway runs again from the top; an index a concurrent build left INVALID is
-- dropped by the migrator and built again.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE t.typname = 'snapshot_owner_kind' AND n.nspname = 'trax'
    ) THEN
        CREATE TYPE trax.snapshot_owner_kind AS ENUM ('user', 'system');
    END IF;
END $$;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS owner_kind trax.snapshot_owner_kind NOT NULL DEFAULT 'user';

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS invoke_token text NULL;

ALTER TABLE trax.snapshot_draft
    ADD COLUMN IF NOT EXISTS row_id bigint NULL;

CREATE SEQUENCE IF NOT EXISTS trax.snapshot_draft_row_id_seq OWNED BY trax.snapshot_draft.row_id;

ALTER TABLE trax.snapshot_draft
    ALTER COLUMN row_id SET DEFAULT nextval('trax.snapshot_draft_row_id_seq');

-- The rows written before the default, a batch at a time. Each batch commits, so no transaction holds
-- more than a batch's rows, and a script that stops partway keeps the batches it filled.
DO $$
DECLARE
    filled integer;
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'snapshot_draft' AND column_name = 'row_id'
          AND is_nullable = 'YES'
    ) THEN
        LOOP
            UPDATE trax.snapshot_draft
            SET row_id = nextval('trax.snapshot_draft_row_id_seq')
            WHERE ctid IN (
                SELECT ctid FROM trax.snapshot_draft WHERE row_id IS NULL LIMIT 10000
            );
            GET DIAGNOSTICS filled = ROW_COUNT;
            EXIT WHEN filled = 0;
            COMMIT;
        END LOOP;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'trax' AND table_name = 'snapshot_draft' AND column_name = 'row_id'
          AND is_nullable = 'YES'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_snapshot_draft_row_id_not_null'
          AND conrelid = 'trax.snapshot_draft'::regclass
    ) THEN
        ALTER TABLE trax.snapshot_draft
            ADD CONSTRAINT ck_snapshot_draft_row_id_not_null CHECK (row_id IS NOT NULL) NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_snapshot_draft_row_id_not_null'
          AND conrelid = 'trax.snapshot_draft'::regclass
          AND NOT convalidated
    ) THEN
        ALTER TABLE trax.snapshot_draft VALIDATE CONSTRAINT ck_snapshot_draft_row_id_not_null;
    END IF;
END $$;

ALTER TABLE trax.snapshot_draft
    ALTER COLUMN row_id SET NOT NULL;

ALTER TABLE trax.snapshot_draft
    DROP CONSTRAINT IF EXISTS ck_snapshot_draft_row_id_not_null;

-- The old key may carry the name the new one takes (051 named it pk_snapshot_draft); renamed, its
-- index is out of the way of the new key's.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'trax.snapshot_draft'::regclass AND contype = 'p'
          AND conname = 'pk_snapshot_draft'
          AND conkey <> ARRAY[(
              SELECT attnum FROM pg_attribute
              WHERE attrelid = 'trax.snapshot_draft'::regclass AND attname = 'row_id'
          )]
    ) THEN
        ALTER TABLE trax.snapshot_draft
            RENAME CONSTRAINT pk_snapshot_draft TO pk_snapshot_draft_user_machine_id;
    END IF;
END $$;

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS pk_snapshot_draft
    ON trax.snapshot_draft (row_id);

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_snapshot_draft_user_machine_id
    ON trax.snapshot_draft (user_key, machine, id)
    WHERE owner_kind = 'user';

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_snapshot_draft_system_machine_id
    ON trax.snapshot_draft (machine, id)
    WHERE owner_kind = 'system';

CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_snapshot_draft_invoke_token
    ON trax.snapshot_draft (invoke_token)
    WHERE invoke_token IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_snapshot_draft_machine_state
    ON trax.snapshot_draft (machine, state);

-- The old key is found by kind rather than by name, as 051 does, because a table an older host built
-- with EF's EnsureCreated carries EF's name for it. The new key takes the index built above, so the
-- swap reads no rows.
DO $$
DECLARE
    old_key text;
    old_columns text[];
BEGIN
    SELECT c.conname,
           array_agg(a.attname::text ORDER BY k.ord)
    INTO old_key, old_columns
    FROM pg_constraint c
    CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord)
    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum
    WHERE c.conrelid = 'trax.snapshot_draft'::regclass AND c.contype = 'p'
    GROUP BY c.conname;

    IF old_columns = ARRAY['row_id'] THEN
        RETURN;
    END IF;

    IF old_key IS NOT NULL THEN
        EXECUTE format('ALTER TABLE trax.snapshot_draft DROP CONSTRAINT %I', old_key);
    END IF;

    ALTER TABLE trax.snapshot_draft
        ADD CONSTRAINT pk_snapshot_draft PRIMARY KEY USING INDEX pk_snapshot_draft;
END $$;

ALTER TABLE trax.snapshot_draft
    ALTER COLUMN user_key DROP NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'trax.snapshot_draft'::regclass
          AND conname = 'ck_snapshot_draft_owner'
    ) THEN
        ALTER TABLE trax.snapshot_draft
            ADD CONSTRAINT ck_snapshot_draft_owner CHECK (
                (owner_kind = 'user' AND user_key IS NOT NULL)
                OR (owner_kind = 'system' AND user_key IS NULL)
            ) NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'trax.snapshot_draft'::regclass
          AND conname = 'ck_snapshot_draft_owner'
          AND NOT convalidated
    ) THEN
        ALTER TABLE trax.snapshot_draft VALIDATE CONSTRAINT ck_snapshot_draft_owner;
    END IF;
END $$;
