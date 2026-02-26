-- Create the Trax.Core schema if it doesn't exist
-- This schema isolates Trax.Core tables from other database objects
create schema if not exists trax;

-- Define the workflow state enum type
-- This enum represents the possible states of a workflow execution
-- Using a PostgreSQL enum type provides type safety at the database level
create type trax.workflow_state as ENUM (
    'pending',     -- Workflow is created but not yet started
    'completed',   -- Workflow has successfully completed
    'failed',      -- Workflow execution failed
    'in_progress'  -- Workflow is currently executing
);

-- Create the metadata table to store workflow execution information
-- This is the primary table for tracking workflow executions in the system
create table trax.metadata
(
    -- Primary key with auto-incrementing ID
    id integer generated always as identity
        constraint workflow_pkey
            primary key,
    
    -- Reference to parent workflow (for nested workflows)
    parent_id integer
        CONSTRAINT workflow_workflow_id_fkey
            REFERENCES trax.metadata (id),
    
    -- External identifier for the workflow (used for lookups)
    external_id char(32) not null,
    
    -- Optional Hangfire job ID for integration with Hangfire
    hangfire_job_id varchar,
    
    -- Name of the workflow (typically the class name)
    name varchar not null,
    
    -- Name of the executor that ran the workflow
    executor varchar,
    
    -- Current state of the workflow execution
    workflow_state trax.workflow_state 
        default 'pending'::trax.workflow_state not null,
    
    -- Number of database changes made during workflow execution
    database_changes integer default 0 not null,
    
    -- Information about workflow failures (if any)
    failure_step varchar,      -- Step where failure occurred
    failure_reason varchar,    -- Reason for failure
    failure_exception varchar, -- Exception details
    stack_trace varchar,       -- Stack trace for debugging
    
    -- Timing information
    start_time timestamp with time zone not null, -- When workflow started
    end_time timestamp with time zone             -- When workflow completed (null if not completed)
);

-- Create a unique index on external_id for efficient lookups
-- This ensures that each workflow has a unique external identifier
create unique index workflow_external_id_uindex
    on trax.metadata (external_id);
