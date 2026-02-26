-- Add optional group_id column to manifest for grouping manifests scheduled together
alter table trax.manifest
    add column group_id varchar;

-- Partial index for efficient group lookups (only indexes non-null values)
create index ix_manifest_group_id on trax.manifest (group_id)
    where group_id is not null;
