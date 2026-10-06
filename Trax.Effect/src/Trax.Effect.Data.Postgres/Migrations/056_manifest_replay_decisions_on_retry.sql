-- Whether a manifest's automatic retries and dead-letter requeues replay the decisions the failed
-- run recorded (054_decision), or ask the decider afresh. On by default, so every existing manifest
-- keeps the replaying behaviour; a manifest whose failures are as likely to come from a decision as
-- from what followed it turns it off.
ALTER TABLE trax.manifest
    ADD COLUMN IF NOT EXISTS replay_decisions_on_retry boolean NOT NULL DEFAULT true;
