-- Create the Trax.Core schema if it doesn't exist
-- This schema isolates Trax.Core tables from other database objects
create schema if not exists trax;

-- Define the train state enum type
-- This enum represents the possible states of a train execution
-- Using a PostgreSQL enum type provides type safety at the database level
create type trax.train_state as ENUM (
    'pending',     -- Train is created but not yet started
    'completed',   -- Train has successfully completed
    'failed',      -- Train execution failed
    'in_progress'  -- Train is currently executing
);

-- Create the metadata table to store train execution information
-- This is the primary table for tracking train executions in the system
create table trax.metadata
(
    -- Primary key with auto-incrementing ID
    id integer generated always as identity
        constraint train_pkey
            primary key,
    
    -- Reference to parent train (for nested trains)
    parent_id integer
        CONSTRAINT train_train_id_fkey
            REFERENCES trax.metadata (id),
    
    -- External identifier for the train (used for lookups)
    external_id char(32) not null,
    
    -- Optional Hangfire job ID for integration with Hangfire
    hangfire_job_id varchar,
    
    -- Name of the train (typically the class name)
    name varchar not null,
    
    -- Name of the executor that ran the train
    executor varchar,
    
    -- Current state of the train execution
    train_state trax.train_state 
        default 'pending'::trax.train_state not null,
    
    -- Number of database changes made during train execution
    database_changes integer default 0 not null,
    
    -- Information about train failures (if any)
    failure_step varchar,      -- Step where failure occurred
    failure_reason varchar,    -- Reason for failure
    failure_exception varchar, -- Exception details
    stack_trace varchar,       -- Stack trace for debugging
    
    -- Timing information
    start_time timestamp with time zone not null, -- When train started
    end_time timestamp with time zone             -- When train completed (null if not completed)
);

-- Create a unique index on external_id for efficient lookups
-- This ensures that each train has a unique external identifier
create unique index train_external_id_uindex
    on trax.metadata (external_id);
