-- See the Postgres migration of the same name: the ChainGraph.Hash of the chain a run started under, null when the
-- host had not read it.
ALTER TABLE metadata ADD COLUMN chain_hash TEXT NULL;
