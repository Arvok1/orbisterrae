CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE accounts (
  id UUID PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE kingdoms (
  id UUID PRIMARY KEY,
  owner_account UUID REFERENCES accounts(id),
  name TEXT NOT NULL,
  culture TEXT NOT NULL,
  personality JSONB,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  collapsed_at TIMESTAMPTZ
);

CREATE TABLE cities (
  id UUID PRIMARY KEY,
  kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  name TEXT NOT NULL,
  pos GEOGRAPHY(Point, 4326) NOT NULL,
  h3_shard BIGINT NOT NULL,
  tier SMALLINT NOT NULL,
  population JSONB NOT NULL,
  importance REAL NOT NULL DEFAULT 0,
  founded_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX cities_pos_idx ON cities USING GIST (pos);
CREATE INDEX cities_h3_shard_idx ON cities (h3_shard);

CREATE TABLE scheduled_events (
  id UUID PRIMARY KEY,
  shard BIGINT NOT NULL,
  due_at TIMESTAMPTZ NOT NULL,
  kind SMALLINT NOT NULL,
  payload JSONB NOT NULL,
  idempotency_key TEXT UNIQUE NOT NULL,
  done_at TIMESTAMPTZ
);

CREATE INDEX scheduled_events_pending_idx
  ON scheduled_events (shard, due_at)
  WHERE done_at IS NULL;

CREATE TABLE event_log (
  seq BIGSERIAL PRIMARY KEY,
  shard BIGINT NOT NULL,
  at TIMESTAMPTZ NOT NULL DEFAULT now(),
  kind SMALLINT NOT NULL,
  payload JSONB NOT NULL
);
