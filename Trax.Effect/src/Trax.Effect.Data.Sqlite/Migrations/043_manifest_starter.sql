-- Who a manifest's runs are started for, as Postgres 084 records it: the hex SHA-256 of "p\0" and the starter's
-- qualified principal id, copied onto every work queue entry the scheduler queues for the manifest.
ALTER TABLE manifest ADD COLUMN starter_hash TEXT NULL;
