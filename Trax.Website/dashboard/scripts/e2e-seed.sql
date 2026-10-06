-- Deterministic seed for the dashboard write-path e2e. Small, known dataset so the mutation
-- flows (cancel / acknowledge / update) have real targets. Re-run to reset to a clean state.
-- Enum values are lowercase in the DB; the GraphQL layer serialises them SCREAMING_SNAKE.

TRUNCATE trax.work_queue, trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group,
  trax.junction_run, trax.decision, trax.log, trax.persisted_operation, trax.persisted_operation_history
  RESTART IDENTITY CASCADE;

INSERT INTO trax.manifest_group (id, name, max_active_jobs, priority, is_enabled, created_at, updated_at)
OVERRIDING SYSTEM VALUE VALUES
  (1, 'e2e-group-alpha', 5, 0, true, now(), now()),
  (2, 'e2e-group-beta', NULL, 3, true, now(), now()),
  (3, 'e2e-group-gamma', NULL, 0, true, now(), now());

-- Manifest 1 carries properties (manifestDetail reads them back, masked where sensitive) and a
-- schedule variance. Manifest 4 exists only for the ask-afresh dead letter (6), so requeueing it
-- queues an entry on a manifest nothing else queues for. Manifest 5 is the batch trigger's target
-- and manifest 6, alone in group 3, the group trigger's, so nothing else queues for either. Manifest
-- 7 runs an admin train, which manifests(hideAdminTrains: true) leaves out.
INSERT INTO trax.manifest
  (id, external_id, name, is_enabled, schedule_type, cron_expression, interval_seconds,
   max_retries, timeout_seconds, priority, manifest_group_id, property_type, properties,
   variance_seconds)
OVERRIDING SYSTEM VALUE VALUES
  (1, 'e2e-manifest-1', 'Trax.E2E.Trains.AlphaTrain', true,  'cron',     '0 */5 * * * *', NULL, 3, 120, 0, 1,
   'Trax.Dashboard.DevHost.PingInput', '{"label": "e2e-seed"}', 30),
  (2, 'e2e-manifest-2', 'Trax.E2E.Trains.BetaTrain',  true,  'interval', NULL,             60, 5, NULL, 0, 1, NULL, NULL, NULL),
  (3, 'e2e-manifest-3', 'Trax.E2E.Trains.GammaTrain', false, 'none',     NULL,           NULL, 1, NULL, 0, 2, NULL, NULL, NULL),
  (4, 'e2e-manifest-4', 'Trax.E2E.Trains.DeltaTrain', true,  'none',     NULL,           NULL, 1, NULL, 0, 2, NULL, NULL, NULL),
  (5, 'e2e-manifest-5', 'Trax.E2E.Trains.EpsilonTrain', true, 'none',    NULL,           NULL, 1, NULL, 0, 1, NULL, NULL, NULL),
  (6, 'e2e-manifest-6', 'Trax.E2E.Trains.ZetaTrain',  true,  'none',     NULL,           NULL, 1, NULL, 0, 3, NULL, NULL, NULL),
  (7, 'e2e-manifest-7', 'Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain', true, 'none',
   NULL, NULL, 0, NULL, 0, 2, NULL, NULL, NULL);

-- manifest_id NULL: a partial unique index allows only one QUEUED entry per manifest, and
-- ad-hoc queued work has no manifest anyway. NULLs don't collide in the index.
-- e2e-wq-6 (id 6) is subject-keyed and staged (no confirmed_at); e2e-wq-7 (id 7) is a dispatched
-- entry for the same subject, so workQueue.detail(6) reports what it waits on. e2e-wq-5 (id 5) ran
-- for manifest 3 and replays run 2's decisions (both set below, once the runs exist).
INSERT INTO trax.work_queue
  (external_id, train_name, status, created_at, priority, dispatch_attempts, manifest_id,
   confirmed_at, subject_key, input, input_type_name, dispatched_at, metadata_id)
VALUES
  ('e2e-wq-1', 'Trax.E2E.Trains.AlphaTrain', 'queued',     now(), 0, 0, NULL, now(), NULL, NULL, NULL, NULL, NULL),
  ('e2e-wq-2', 'Trax.E2E.Trains.AlphaTrain', 'queued',     now(), 5, 0, NULL, now(), NULL, NULL, NULL, NULL, NULL),
  ('e2e-wq-3', 'Trax.E2E.Trains.BetaTrain',  'queued',     now(), 0, 0, NULL, now(), NULL, NULL, NULL, NULL, NULL),
  ('e2e-wq-4', 'Trax.E2E.Trains.BetaTrain',  'queued',     now(), 0, 1, NULL, now(), NULL, NULL, NULL, NULL, NULL),
  ('e2e-wq-5', 'Trax.E2E.Trains.GammaTrain', 'dispatched', now(), 0, 1, NULL, now(), NULL, NULL, NULL, now(), NULL),
  ('e2e-wq-6', 'Trax.E2E.Trains.SubjectTrain', 'queued',   now(), 0, 0, NULL, NULL, 'order-42',
   '{"label": "subject"}', 'Trax.Dashboard.DevHost.PingInput', NULL, NULL),
  ('e2e-wq-7', 'Trax.E2E.Trains.SubjectTrain', 'dispatched', now() - interval '1 minute', 0, 1, NULL,
   now() - interval '1 minute', 'order-42', NULL, NULL, now() - interval '1 minute', NULL);

-- Each AWAITING dead letter references a DISTINCT manifest. Requeue (single or all) creates a
-- QUEUED work_queue entry per dead letter carrying its manifest_id, and the partial unique index
-- allows only one QUEUED entry per manifest. Two awaiting dead letters for the same manifest would
-- therefore make requeue-all fail on the unique constraint, so keep them on separate manifests.
INSERT INTO trax.dead_letter
  (id, manifest_id, dead_lettered_at, status, reason, retry_count_at_dead_letter)
OVERRIDING SYSTEM VALUE VALUES
  (1, 1, now(), 'awaiting_intervention', 'E2E seeded failure 1', 3),
  (2, 2, now(), 'awaiting_intervention', 'E2E seeded failure 2', 3),
  (3, 3, now(), 'awaiting_intervention', 'E2E seeded failure 3', 2),
  (4, 2, now(), 'retried',               'E2E already retried',  3),
  (5, 3, now(), 'acknowledged',          'E2E already acked',    1),
  (6, 4, now(), 'awaiting_intervention', 'E2E ask-afresh target', 1);

-- Run 2 failed (transient) on manifest 2, with three recorded steps (junction_run below) and four
-- recorded decisions (decision below). Run 6 is a child of run 1 that was queued to replay run 2's
-- decisions and asked afresh instead (replay_abandoned), with the run-detail fields set. Run 7 is a
-- completed run of a train the devhost registers, with input, so requeueExecution can queue it.
-- Run 3 is running its LoadCart junction; run 4 runs for manifest 2, so cancelGroups([1]) reaches it.
INSERT INTO trax.metadata
  (id, external_id, name, train_state, start_time, end_time, manifest_id, failure_class,
   failure_junction, failure_reason, failure_exception, stack_trace, input)
OVERRIDING SYSTEM VALUE VALUES
  (1, 'e2e-meta-1', 'Trax.E2E.Trains.AlphaTrain', 'completed',   now() - interval '60 minutes', now() - interval '59 minutes', NULL, 'unclassified', NULL, NULL, NULL, NULL, NULL),
  (2, 'e2e-meta-2', 'Trax.E2E.Trains.BetaTrain',  'failed',      now() - interval '30 minutes', now() - interval '29 minutes', 2, 'transient',
   'ChargeCard', 'gateway timed out', 'TimeoutException', 'at Trax.E2E.ChargeCard()', '{"label": "beta"}'),
  (3, 'e2e-meta-3', 'Trax.E2E.Trains.AlphaTrain', 'in_progress', now() - interval '5 minutes',  NULL, NULL, 'unclassified', NULL, NULL, NULL, NULL, NULL),
  (4, 'e2e-meta-4', 'Trax.E2E.Trains.BetaTrain',  'in_progress', now() - interval '3 minutes',  NULL, NULL, 'unclassified', NULL, NULL, NULL, NULL, NULL),
  (5, 'e2e-meta-5', 'Trax.E2E.Trains.GammaTrain', 'pending',     now() - interval '1 minute',   NULL, NULL, 'unclassified', NULL, NULL, NULL, NULL, NULL);

INSERT INTO trax.metadata
  (id, external_id, name, train_state, start_time, end_time, parent_id, executor, scheduled_time,
   host_name, host_environment, host_instance_id, host_labels, replay_decisions_of, replay_abandoned,
   failure_class)
OVERRIDING SYSTEM VALUE VALUES
  (6, 'e2e-meta-6', 'Trax.E2E.Trains.AlphaTrain', 'completed', now() - interval '58 minutes', now() - interval '57 minutes',
   1, 'JobRunner', now() - interval '58 minutes', 'e2e-host', 'Development', 'e2e-instance',
   '{"region": "eu-west"}', 2, true, 'unclassified');

INSERT INTO trax.metadata (id, external_id, name, train_state, start_time, end_time, input, failure_class)
OVERRIDING SYSTEM VALUE VALUES
  (7, 'e2e-meta-7', 'Trax.Dashboard.DevHost.IBroadcastPingTrain', 'completed',
   now() - interval '20 minutes', now() - interval '19 minutes', '{"label": "requeue-me"}', 'unclassified');

-- e2e-wq-7 is running as run 4 (linked once the run exists).
UPDATE trax.work_queue SET metadata_id = 4 WHERE external_id = 'e2e-wq-7';
UPDATE trax.work_queue SET manifest_id = 3, replay_decisions_of = 2 WHERE external_id = 'e2e-wq-5';
UPDATE trax.metadata SET currently_running_junction = 'LoadCart', junction_started_at = now() WHERE id = 3;
UPDATE trax.metadata SET manifest_id = 2 WHERE id = 4;

-- Run 2's steps: a junction that completed, a yes/no question that was replayed, and the junction
-- that failed. Positions start at 0, as the writer's do.
INSERT INTO trax.junction_run
  (metadata_id, position, kind, name, state, started_at, ended_at, failure_class, failure_exception,
   question_key, answer, confidence, replayed, attempt)
VALUES
  (2, 0, 'junction', 'ValidateOrder', 'completed', now() - interval '30 minutes', now() - interval '30 minutes' + interval '2 seconds',
   NULL, NULL, NULL, NULL, NULL, false, 2),
  (2, 1, 'yes_no', 'IsFraud', 'completed', now() - interval '30 minutes' + interval '2 seconds', NULL,
   NULL, NULL, 'is-fraud', 'no', 0.92, true, 2),
  (2, 2, 'junction', 'ChargeCard', 'failed', now() - interval '30 minutes' + interval '3 seconds', now() - interval '29 minutes',
   'transient', 'TimeoutException', NULL, NULL, NULL, false, 2);

-- Run 2's recorded decisions, in the order it made them: a yes/no a model answered; a score whose
-- replay was refused, so it was asked afresh; a choice about DevhostCarrier, a [TraxSensitive] type
-- the devhost declares, whose answer is withheld on read and whose route puts every later decision
-- on a withheld track; and one asked on that track. Run 6 recorded one replayed answer it refused.
INSERT INTO trax.decision
  (metadata_id, question_key, occurrence, fingerprint, kind, question, answer, model, decider, replayed,
   routes, state_hash, refused, decided_at)
VALUES
  (2, 'is-fraud', 0, 'e2e-fp-fraud', 'yes_no', '{"question": "Is this order fraudulent?"}', '"no"',
   'e2e-model', 'Trax.E2E.FraudDecider', false, NULL, 'k1:e2e-fraud', NULL, now() - interval '30 minutes'),
  (2, 'risk', 0, 'e2e-fp-risk', 'score', '{"question": "How risky is this order?"}',
   '{"score": 0.2, "replay_refused": "the question changed since the run it replays"}',
   'e2e-model', 'Trax.E2E.RiskDecider', false, NULL, NULL, NULL, now() - interval '30 minutes' + interval '1 second'),
  (2, 'DevhostCarrier', 0, 'e2e-fp-carrier', 'choice', '{"question": "Which carrier?"}', '"Air"',
   'e2e-model', 'Trax.E2E.CarrierDecider', false, '[{"track": "air", "fallback_reason": null}]', 's1:e2e-carrier',
   NULL, now() - interval '30 minutes' + interval '2 seconds'),
  (2, 'notify', 0, 'e2e-fp-notify', 'yes_no', '{"question": "Notify the customer?"}', '"yes"',
   'e2e-model', 'Trax.E2E.NotifyDecider', false, NULL, NULL, NULL, now() - interval '30 minutes' + interval '3 seconds'),
  (6, 'is-fraud', 0, 'e2e-fp-fraud', 'yes_no', '{"question": "Is this order fraudulent?"}', '"maybe"',
   NULL, NULL, true, NULL, NULL, 'the answer is not one of yes or no', now() - interval '58 minutes');

-- Run 2's log, for the run page (oldest first) and the text filters, and 10,005 heartbeat entries
-- with no run, so a text filter matching them stops counting at 10,000 (isCountCapped). The log
-- writer never sets metadata_id, so a real entry with no run carries 0, as these do; logs reads it
-- as a Long!, and a NULL there fails the read.
INSERT INTO trax.log (metadata_id, event_id, level, message, category, exception, stack_trace)
VALUES
  (2, 0, 'information', 'Charging card for order 42', 'Trax.E2E.Payments.Gateway', NULL, NULL),
  (2, 0, 'debug', 'Payment context loaded', 'Trax.E2E.Context', NULL, NULL),
  (2, 0, 'warning', 'Gateway slow, retrying', 'Trax.E2E.Payments.Gateway', NULL, NULL),
  (2, 0, 'error', 'Gateway timed out after 30s', 'Trax.E2E.Payments.Gateway', 'TimeoutException',
   'at Trax.E2E.ChargeCard()');
INSERT INTO trax.log (metadata_id, event_id, level, message, category)
SELECT 0, 0, 'trace', 'heartbeat tick ' || g, 'Trax.E2E.Heartbeat' FROM generate_series(1, 10005) AS g;

-- Persisted operations: two in the default tenant (one deactivated) and one in a named tenant.
INSERT INTO trax.persisted_operation
  (tenant_key, id, operation_name, version, document, shape_fingerprint, is_active, deprecation_reason, description)
VALUES
  ('', 'e2e.greet.v1', 'Greet', 1, 'query Greet { operations { health { status } } }', 'e2e-fingerprint-greet', true, NULL, 'E2E health probe'),
  ('', 'e2e.greet.v0', 'Greet', 0, 'query Greet { operations { health { description } } }', 'e2e-fingerprint-old', false, 'superseded', NULL),
  ('acme', 'e2e.orders', 'Orders', 3, 'query Orders { operations { trains { fullName } } }', 'e2e-fingerprint-orders', true, NULL, NULL);

INSERT INTO trax.persisted_operation_history (tenant_key, id, document, shape_fingerprint, change_type, changed_reason)
VALUES
  ('', 'e2e.greet.v1', 'query Greet { operations { health { status } } }', 'e2e-fingerprint-greet', 'Upsert', NULL),
  ('', 'e2e.greet.v0', 'query Greet { operations { health { description } } }', 'e2e-fingerprint-old', 'Deactivate', 'superseded');

-- Explicit ids above do not move the identity sequences; move them past the seeded rows so rows the
-- devhost creates (runs, dead letters, manifests) do not collide with them.
SELECT setval(pg_get_serial_sequence('trax.manifest_group', 'id'), (SELECT max(id) FROM trax.manifest_group));
SELECT setval(pg_get_serial_sequence('trax.manifest', 'id'), (SELECT max(id) FROM trax.manifest));
SELECT setval(pg_get_serial_sequence('trax.dead_letter', 'id'), (SELECT max(id) FROM trax.dead_letter));
SELECT setval(pg_get_serial_sequence('trax.metadata', 'id'), (SELECT max(id) FROM trax.metadata));
