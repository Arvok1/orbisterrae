ALTER TABLE accounts ALTER COLUMN email DROP NOT NULL;
ALTER TABLE accounts ADD COLUMN username TEXT UNIQUE;
ALTER TABLE accounts ADD COLUMN password_hash TEXT;
CREATE UNIQUE INDEX one_kingdom_per_account ON kingdoms(owner_account) WHERE owner_account IS NOT NULL;
ALTER TABLE cities ADD COLUMN state JSONB;
CREATE TABLE entities (
  id UUID PRIMARY KEY,
  kind SMALLINT NOT NULL,
  kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  shard BIGINT NOT NULL,
  state JSONB NOT NULL
);
CREATE UNIQUE INDEX one_ruler_per_kingdom ON entities(kingdom_id) WHERE kind = 0;
CREATE INDEX entities_shard_idx ON entities(shard);
CREATE TABLE territory_cells (
  h3_res7 BIGINT PRIMARY KEY,
  city_id UUID NOT NULL REFERENCES cities(id),
  influence DOUBLE PRECISION NOT NULL
);
ALTER TABLE scheduled_events ADD COLUMN worker_slot SMALLINT;
CREATE INDEX scheduled_events_worker_due_idx ON scheduled_events(worker_slot,due_at) WHERE done_at IS NULL;
CREATE TABLE command_receipts (
  account_id UUID NOT NULL REFERENCES accounts(id),
  request_id TEXT NOT NULL,
  response BYTEA NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY(account_id,request_id)
);
CREATE TABLE entity_handoffs (
  entity_id UUID NOT NULL REFERENCES entities(id),
  revision BIGINT NOT NULL,
  from_shard BIGINT NOT NULL,
  to_shard BIGINT NOT NULL,
  source_worker SMALLINT NOT NULL,
  state BYTEA NOT NULL,
  acknowledged_at TIMESTAMPTZ,
  PRIMARY KEY(entity_id,revision)
);
CREATE INDEX entity_handoffs_pending ON entity_handoffs(source_worker) WHERE acknowledged_at IS NULL;
