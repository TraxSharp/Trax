-- 1. Create manifest_group table
CREATE TABLE trax.manifest_group (
    id serial PRIMARY KEY,
    name varchar NOT NULL,
    max_active_jobs int,
    priority smallint NOT NULL DEFAULT 0,
    is_enabled boolean NOT NULL DEFAULT true,
    created_at timestamp NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    updated_at timestamp NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    CONSTRAINT uq_manifest_group_name UNIQUE (name)
);

-- 2. Seed from existing group_id values (non-null)
INSERT INTO trax.manifest_group (name)
SELECT DISTINCT group_id FROM trax.manifest WHERE group_id IS NOT NULL;

-- 3. Seed auto-generated groups for ungrouped manifests (using external_id)
INSERT INTO trax.manifest_group (name)
SELECT DISTINCT external_id FROM trax.manifest WHERE group_id IS NULL;

-- 4. Set group priority from max manifest priority
UPDATE trax.manifest_group mg SET priority = sub.max_p
FROM (
    SELECT COALESCE(m.group_id, m.external_id) AS gname, MAX(m.priority) AS max_p
    FROM trax.manifest m GROUP BY 1
) sub WHERE mg.name = sub.gname AND sub.max_p > 0;

-- 5. Add FK column, populate, make NOT NULL
ALTER TABLE trax.manifest ADD COLUMN manifest_group_id int;

UPDATE trax.manifest m SET manifest_group_id = mg.id
FROM trax.manifest_group mg WHERE mg.name = COALESCE(m.group_id, m.external_id);

ALTER TABLE trax.manifest ALTER COLUMN manifest_group_id SET NOT NULL;
ALTER TABLE trax.manifest
    ADD CONSTRAINT fk_manifest_manifest_group
    FOREIGN KEY (manifest_group_id) REFERENCES trax.manifest_group(id);
CREATE INDEX ix_manifest_manifest_group_id ON trax.manifest (manifest_group_id);

-- 6. Drop old group_id column
DROP INDEX IF EXISTS trax.ix_manifest_group_id;
ALTER TABLE trax.manifest DROP COLUMN group_id;
