-- One row per running host process, rewritten every 15 seconds whether or not it has work, so an
-- idle host and a dead one can be told apart. The model is Trax.Effect.Models.HostHeartbeat, on
-- IDataContext.HostHeartbeats. Every host with a data provider writes its own row from a background
-- service; operations.hosts reads the table and calls a host offline once its last beat is 45
-- seconds old (three missed beats). Any host's beat deletes the rows whose last beat is a day old.
--
-- instance_id: TraxHostInfo.HostInstanceId, the identity stamped on each run's metadata (a pod name,
--   an ECS task id, or machine name and process id). The primary key: a process writes one row.
-- host_name, host_environment: TraxHostInfo.HostName and HostEnvironment.
-- trax_version: the Trax version the process runs.
-- started_at: when the process began beating. A restarted process that reuses the instance id
--   rewrites it.
-- last_heartbeat_at: when the process last wrote the row.
-- runs_dispatcher, runs_manifest_manager: whether the process runs the scheduler's job dispatcher
--   and manifest manager, and has each switched on, at the last beat.
-- worker_slots, worker_slots_in_use: how many local workers the process runs and how many were
--   running a job at the last beat; null on a process that runs none.
--
-- Every statement checks before it changes anything, so the script can run again over its own result.
CREATE TABLE IF NOT EXISTS trax.host_heartbeat (
    instance_id text PRIMARY KEY,
    host_name text NOT NULL,
    host_environment text NOT NULL,
    trax_version text NOT NULL,
    started_at timestamptz NOT NULL DEFAULT now(),
    last_heartbeat_at timestamptz NOT NULL DEFAULT now(),
    runs_dispatcher boolean NOT NULL DEFAULT false,
    runs_manifest_manager boolean NOT NULL DEFAULT false,
    worker_slots integer,
    worker_slots_in_use integer
);

-- The prune deletes by age and the hosts read orders by it. The table holds one row per process
-- seen in the last day, so it is small and needs no concurrent build.
CREATE INDEX IF NOT EXISTS ix_host_heartbeat_last_heartbeat_at
    ON trax.host_heartbeat (last_heartbeat_at);
