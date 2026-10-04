-- Which routing step's track a junction ran on (the position of the latest route before it), and
-- whether its name is withheld because that track's answer is: which junctions ran would give a
-- withheld answer away.
ALTER TABLE trax.junction_run ADD COLUMN IF NOT EXISTS name_withheld boolean NOT NULL DEFAULT false;
ALTER TABLE trax.junction_run ADD COLUMN IF NOT EXISTS track_position integer;
