-- See the Postgres migration of the same name.
ALTER TABLE effect_claim ADD COLUMN content_fingerprint TEXT;
