-- Create the dead_letter_status enum for tracking resolution state
create type trax.dead_letter_status as enum ('awaiting_intervention', 'retried', 'acknowledged');

-- Create the dead_letter table for tracking jobs that have exceeded retry limits
-- Dead letters require manual intervention - they won't be automatically retried
create table if not exists trax.dead_letter
(
    -- Primary key with auto-incrementing ID
    id integer generated always as identity
        constraint dead_letter_pkey
            primary key,
    
    -- Foreign key to the manifest (job definition)
    manifest_id integer not null
        constraint dead_letter_manifest_id_fkey
            references trax.manifest (id)
            on delete restrict,
    
    -- When this job was moved to the dead letter queue
    dead_lettered_at timestamptz not null default now(),
    
    -- Current resolution status
    status trax.dead_letter_status not null default 'awaiting_intervention',
    
    -- When this dead letter was resolved (retried or acknowledged)
    resolved_at timestamptz,
    
    -- Operator notes explaining the resolution
    resolution_note varchar,
    
    -- Reason why this job was dead-lettered
    reason varchar not null,
    
    -- Number of retry attempts made before dead-lettering
    retry_count_at_dead_letter integer not null,
    
    -- Foreign key to the new metadata record if this was retried
    retry_metadata_id integer
        constraint dead_letter_retry_metadata_id_fkey
            references trax.metadata (id)
            on delete restrict
);

-- Create index on manifest_id for looking up dead letters by job definition
create index if not exists dead_letter_manifest_id_idx 
    on trax.dead_letter (manifest_id);

-- Create index on status for querying unresolved dead letters
create index if not exists dead_letter_status_idx
    on trax.dead_letter (status)
    where status = 'awaiting_intervention';

-- Create index on dead_lettered_at for time-based queries and cleanup
create index if not exists dead_letter_dead_lettered_at_idx
    on trax.dead_letter (dead_lettered_at);
