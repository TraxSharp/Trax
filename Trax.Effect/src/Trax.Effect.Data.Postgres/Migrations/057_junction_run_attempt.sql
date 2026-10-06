-- Which attempt of its manifest a run is, as its junction events carry it: 1 plus the manifest's
-- failed runs since its last completed or cancelled one. Null for a run with no manifest, and for a
-- step written before the column existed or whose attempt could not be read.
ALTER TABLE trax.junction_run ADD COLUMN IF NOT EXISTS attempt integer;
