-- Add dead_letter_id FK to work_queue for linking requeued entries back to their dead letter
ALTER TABLE trax.work_queue ADD COLUMN IF NOT EXISTS dead_letter_id bigint
    REFERENCES trax.dead_letter(id) ON DELETE RESTRICT;
