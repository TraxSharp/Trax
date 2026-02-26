-- Add input column to store workflow input parameters
-- Using PostgreSQL's jsonb type for flexible JSON storage with indexing capabilities
-- This allows storing structured input data of any shape without schema changes
alter table trax.metadata
    add input jsonb;

-- Add output column to store workflow execution results
-- Using PostgreSQL's jsonb type for flexible JSON storage with indexing capabilities
-- This allows storing structured output data of any shape without schema changes
alter table trax.metadata
    add output jsonb;
