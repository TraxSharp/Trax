-- The fires machine policies still owe: Postgres 090, which says what each column holds. The actor of a policy's
-- transition is stored as its integer (3), so the actor needs no change here.
CREATE TABLE IF NOT EXISTS machine_policy_fire (
    id INTEGER NOT NULL CONSTRAINT pk_machine_policy_fire PRIMARY KEY AUTOINCREMENT,
    policy TEXT NOT NULL,
    source_row_id INTEGER NOT NULL,
    source_instance_id TEXT NOT NULL,
    source_revision INTEGER NOT NULL,
    target_row_id INTEGER NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_machine_policy_fire_policy ON machine_policy_fire (policy, id);
