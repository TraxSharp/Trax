-- A run's decisions in the order it made them, by id. The dashboard and the GraphQL operations read
-- them a page at a time, and uq_decision_run_question leads on metadata_id but is ordered by
-- question, so the planner walks the primary key in id order and filters on metadata_id instead:
-- past every other run's decisions before the first of this one's (about 290 ms for the first page
-- of a run at a million decisions, growing with the table). This index hands over a run's
-- decisions already in id order. Built CONCURRENTLY so recording decisions carries on while it
-- builds (effect/0014).
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_decision_metadata_id_id
    ON trax.decision (metadata_id, id);
