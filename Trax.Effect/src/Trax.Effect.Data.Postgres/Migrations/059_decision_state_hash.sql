-- The hash of the state each recorded question was asked about, as Trax.Core reports it: 'k1:' and
-- the lowercase hex of an HMAC-SHA256 under the host's state hash key, or 's1:' and the lowercase
-- hex of a SHA-256 without one. A requeued run's replay hands it back, and Trax.Core replays an
-- answer only into a state that hashes the same. It is null when the state could not be encoded,
-- and when no key is configured and the state can hold a value marked [TraxSensitive]. Rows written
-- before this column have none. An answer with no hash is asked afresh rather than replayed.
ALTER TABLE trax.decision ADD COLUMN IF NOT EXISTS state_hash text;
