-- Add manifest_id foreign key column to metadata table
-- This establishes the relationship between train executions (metadata) and job definitions (manifest)
-- A single manifest (job definition) can have many metadata records (train executions)

-- Add the manifest_id column to the metadata table
alter table trax.metadata
    add column if not exists manifest_id integer;

-- Add the foreign key constraint
alter table trax.metadata
    add constraint metadata_manifest_id_fkey
        foreign key (manifest_id)
            references trax.manifest (id)
            on delete restrict;

-- Create index on manifest_id for faster lookups
create index if not exists metadata_manifest_id_idx 
    on trax.metadata (manifest_id);

-- Add scheduled_time column for tracking when the job was supposed to run
-- This is distinct from start_time (when it actually started) - useful for SLA tracking
alter table trax.metadata
    add column if not exists scheduled_time timestamptz;
