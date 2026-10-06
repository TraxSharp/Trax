CREATE TYPE trax.misfire_policy AS ENUM ('fire_once_now', 'do_nothing');

ALTER TABLE trax.manifest
    ADD COLUMN misfire_policy trax.misfire_policy NOT NULL DEFAULT 'fire_once_now';

ALTER TABLE trax.manifest
    ADD COLUMN misfire_threshold_seconds integer;
