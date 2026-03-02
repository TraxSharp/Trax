-- Create the schedule_type enum for manifest scheduling strategies
create type trax.schedule_type as enum ('none', 'cron', 'interval', 'on_demand');

-- Create the manifest table to store train manifest information
-- This table stores configuration, scheduling, and retry policies for trains
create table if not exists trax.manifest
(
    -- Primary key with auto-incrementing ID
    id integer generated always as identity
        constraint manifest_pkey
            primary key,
    
    -- External identifier for the manifest (used for external lookups)
    external_id char(32) not null,
    
    -- Name of the type this manifest represents (typically the full type name)
    name varchar not null,
    
    -- Full type name of the property serializer type
    property_type varchar,
    
    -- JSON properties stored as JSONB for flexible configuration
    properties jsonb,
    
    -- Scheduling columns
    
    -- Whether this manifest is enabled for scheduling (allows pausing without deleting)
    is_enabled boolean not null default true,
    
    -- The scheduling strategy: none (manual), cron, interval, or on_demand (bulk)
    schedule_type trax.schedule_type not null default 'none',
    
    -- Cron expression for cron-type schedules (e.g., "0 3 * * *" for daily at 3am)
    cron_expression varchar,
    
    -- Interval in seconds for interval-type schedules
    interval_seconds integer,
    
    -- Maximum retry attempts before dead-lettering
    max_retries integer not null default 3,
    
    -- Timeout in seconds for job execution (null = use global default)
    timeout_seconds integer,
    
    -- Timestamp of the last successful execution
    last_successful_run timestamptz
);

-- Create index on external_id for faster lookups
create index if not exists manifest_external_id_idx 
    on trax.manifest (external_id);

-- Create index on name for faster lookups by type
create index if not exists manifest_name_idx 
    on trax.manifest (name);

-- Create index on is_enabled and schedule_type for efficient polling queries
create index if not exists manifest_scheduling_idx
    on trax.manifest (is_enabled, schedule_type)
    where is_enabled = true;
