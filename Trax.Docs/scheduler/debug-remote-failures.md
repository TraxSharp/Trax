---
title: Debug a failed remote run
description: "Where to look, in order, when a job dispatched to a remote runner failed: the run's metadata row, its log, a stale pending run and a requeued dispatch."
type: how-to
area: scheduling
nav_order: 8
---

# Debug a failed remote run

Start from the run's id, which the dashboard's run page and the `operations.executions` query show.

1. **Read the run's row.** The failure fields are filled from the runner's structured response:

   ```sql
   SELECT train_state, failure_exception, failure_junction, failure_reason, failure_class
   FROM trax.metadata WHERE id = <id>;
   ```

   `failure_junction` names the junction that threw, and `failure_exception` its type. The stack
   trace is not on this row for a `/trax/run` failure; it is in the runner's log.

2. **Read its log.** If the runner registers `AddDataContextLogging()`, junction-level logs are
   persisted:

   ```sql
   SELECT * FROM trax.log WHERE metadata_id = <id> ORDER BY id;
   ```

3. **Check whether the runner ever started it.** `failure_exception = 'StalePendingTimeout'` means
   the run stayed `Pending` until the stale-pending reaper failed it: the runner never began. Check
   the runner's health, the network path between the two, and whether the runner is deployed.

4. **Check whether the dispatch was requeued.** `failure_exception = 'DispatchRequeued'` means that
   attempt never reached a runner and the work queue entry was queued again; `failure_reason` holds
   the submitter's error. Such a run does not count against the manifest's retries. A run whose
   `failure_junction` is `DispatchJobsJunction` with any other exception is the attempt that used
   up `MaxDispatchAttempts`, and counts once.

5. **Check whether the runner refused it.** A refused request never reaches a train. Over HTTP the
   runner answers `401` (a signature or policy mismatch) or `413` (a body over its limit), so the
   dispatch fails and step 4 applies. A refused Lambda `Execute` invocation or SQS message throws on
   the runner after the scheduler has handed it over, so the row stays `Pending` until step 3's
   reaper fails it. The runner logs refusals as a warning, at most once a minute with a count. See
   [Runner authorization posture](/docs/scheduler/runner-authorization#refusals).

The fields of the response are in [Remote error responses](/docs/scheduler/remote-error-responses),
and the dispatcher's handling of a failed submit in
[JobDispatcher: Dispatch failures](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures).

## SDK Reference

> [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [StalePendingTimeout / MaxDispatchAttempts](/docs/sdk-reference/scheduler-api/add-scheduler)
