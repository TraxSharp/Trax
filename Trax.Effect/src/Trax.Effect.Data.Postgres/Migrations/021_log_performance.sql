-- Restore primary key (dropped by migration 004)
ALTER TABLE trax.log ADD CONSTRAINT log_pkey PRIMARY KEY (id);

-- Index for dashboard MetadataDetailPage and DeleteExpiredMetadataStep queries
CREATE INDEX ix_log_metadata_id ON trax.log (metadata_id);
