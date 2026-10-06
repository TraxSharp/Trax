ALTER TABLE trax.metadata RENAME COLUMN failure_step TO failure_junction;
ALTER TABLE trax.metadata RENAME COLUMN step_started_at TO junction_started_at;
ALTER TABLE trax.metadata RENAME COLUMN currently_running_step TO currently_running_junction;
