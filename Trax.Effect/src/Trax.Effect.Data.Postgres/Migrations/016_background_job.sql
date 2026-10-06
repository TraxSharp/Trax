CREATE TABLE trax.background_job (
    id            bigserial    PRIMARY KEY,
    metadata_id   int          NOT NULL,
    input         jsonb,
    input_type    varchar(512),
    created_at    timestamptz  NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    fetched_at    timestamptz
);

CREATE INDEX ix_background_job_dequeue
    ON trax.background_job (created_at ASC)
    WHERE fetched_at IS NULL;
