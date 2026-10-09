-- The Parallel branch each decision was asked in and each step ran in, as Trax.Core names it
-- (Parallel#0/cocitation). A branch counts its askings of a question on from where it forked, so two
-- branches asking one question ask it under the same occurrence: the branch is part of what tells
-- one asking from another, for the row's uniqueness and for a requeue replaying it. Empty for a
-- decision outside any branch, which every row written before this was.
ALTER TABLE trax.decision ADD COLUMN IF NOT EXISTS branch_path text NOT NULL DEFAULT '';

-- Built before the old key is dropped, so a run's askings stay unique throughout. CONCURRENTLY so
-- recording decisions carries on while it builds (effect/0014).
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_decision_run_branch_question
    ON trax.decision (metadata_id, branch_path, question_key, occurrence);

ALTER TABLE trax.decision DROP CONSTRAINT IF EXISTS uq_decision_run_question;

-- So a run's timeline can be drawn in a lane per branch. Null outside any branch, for a step whose
-- name is withheld, and for rows written before this.
ALTER TABLE trax.junction_run ADD COLUMN IF NOT EXISTS branch_path text;
