ALTER TABLE accounts
  ADD COLUMN role TEXT NOT NULL DEFAULT 'player'
  CHECK (role IN ('player', 'admin'));

CREATE INDEX accounts_role_idx ON accounts(role) WHERE role = 'admin';

CREATE TABLE admin_audit (
  id BIGSERIAL PRIMARY KEY,
  account_id UUID NOT NULL REFERENCES accounts(id),
  action TEXT NOT NULL,
  filters JSONB NOT NULL DEFAULT '{}'::jsonb,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX admin_audit_account_time_idx ON admin_audit(account_id, occurred_at DESC);

ALTER TABLE event_log ADD COLUMN account_id UUID REFERENCES accounts(id);
ALTER TABLE event_log ADD COLUMN kingdom_id UUID REFERENCES kingdoms(id);
ALTER TABLE event_log ADD COLUMN city_id UUID REFERENCES cities(id);
CREATE INDEX event_log_admin_filter_idx ON event_log(account_id, kingdom_id, city_id, seq);

-- Keep the res7 table for rollback and existing readers while res8 is populated.
CREATE TABLE territory_cells_res8 (
  h3_res8 BIGINT PRIMARY KEY,
  city_id UUID NOT NULL REFERENCES cities(id),
  influence DOUBLE PRECISION NOT NULL
);
CREATE INDEX territory_cells_res8_city_idx ON territory_cells_res8(city_id);

ALTER TABLE city_layouts ADD COLUMN grid_version SMALLINT NOT NULL DEFAULT 1;
UPDATE city_layouts SET grid_version = 2 WHERE state->>'width' = '256';
