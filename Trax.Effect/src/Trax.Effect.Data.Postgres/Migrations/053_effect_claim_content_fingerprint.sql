-- Binds an effect's claim to the content the effect ran on. The state-machine effect runner records a SHA-256
-- of the draft's canonical wire when it takes the claim, and replays the claim's receipt only onto a draft whose
-- content still has that fingerprint. A draft that changed after its effect ran is refused rather than committed
-- with a receipt for other content.
--
-- Nullable, with no backfill: an existing claim has no fingerprint and replays its receipt as it did before, and
-- a host still running the previous version reads and writes the table unchanged, so a rolling deploy is safe.
ALTER TABLE trax.effect_claim ADD COLUMN IF NOT EXISTS content_fingerprint text;
