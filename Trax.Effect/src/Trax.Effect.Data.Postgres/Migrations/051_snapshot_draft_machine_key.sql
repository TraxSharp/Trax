-- Scopes a state-machine draft to its machine (Trax.Effect.StateMachine.Persistence). The key was
-- (user_key, id), but the draft id is client-chosen and a machine may use one well-known id per user, so two
-- machines' drafts under one id shared a row: the second machine's save overwrote the first's draft, and its
-- read found a draft for another machine. The machine joins the key, and the store reads and writes by it.
--
-- Existing rows are unchanged: each already names its machine, and no two rows can collide under the wider
-- key. The old key is found by kind rather than by name, because a table an older host built with EF's
-- EnsureCreated carries EF's name for it rather than 040's. The block checks the current key first, so a
-- second run changes nothing.
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

    IF old_columns = ARRAY['user_key', 'machine', 'id'] THEN
        RETURN;
    END IF;

    IF old_key IS NOT NULL THEN
        EXECUTE format('ALTER TABLE trax.snapshot_draft DROP CONSTRAINT %I', old_key);
    END IF;

    ALTER TABLE trax.snapshot_draft
        ADD CONSTRAINT pk_snapshot_draft PRIMARY KEY (user_key, machine, id);
END $$;
