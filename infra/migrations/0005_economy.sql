-- Economy tables for S1.3
CREATE TABLE hubs (
  id UUID PRIMARY KEY,
  name TEXT NOT NULL,
  pos GEOGRAPHY(Point, 4326) NOT NULL,
  h3_shard BIGINT NOT NULL,
  kingdom_id UUID REFERENCES kingdoms(id),
  stock JSONB NOT NULL DEFAULT '{}',
  metal BIGINT NOT NULL DEFAULT 0,
  fee NUMERIC(5,4) NOT NULL DEFAULT 0.02
);

CREATE INDEX hubs_pos_idx ON hubs USING GIST (pos);
CREATE INDEX hubs_h3_shard_idx ON hubs (h3_shard);
CREATE INDEX hubs_kingdom_idx ON hubs (kingdom_id) WHERE kingdom_id IS NOT NULL;

CREATE TABLE caravans (
  id UUID PRIMARY KEY,
  kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  from_city UUID NOT NULL REFERENCES cities(id),
  to_hub UUID NOT NULL REFERENCES hubs(id),
  journey JSONB,
  cargo JSONB NOT NULL DEFAULT '{}',
  metal BIGINT NOT NULL DEFAULT 0,
  revision BIGINT NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'queued',
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX caravans_kingdom_idx ON caravans (kingdom_id);
CREATE INDEX caravans_from_city_idx ON caravans (from_city);
CREATE INDEX caravans_to_hub_idx ON caravans (to_hub);
CREATE INDEX caravans_status_idx ON caravans (status) WHERE status IN ('queued', 'traveling', 'at_hub');

CREATE TABLE ships (
  id UUID PRIMARY KEY,
  kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  from_city UUID NOT NULL REFERENCES cities(id),
  to_hub UUID NOT NULL REFERENCES hubs(id),
  journey JSONB,
  cargo JSONB NOT NULL DEFAULT '{}',
  metal BIGINT NOT NULL DEFAULT 0,
  revision BIGINT NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'queued',
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ships_kingdom_idx ON ships (kingdom_id);
CREATE INDEX ships_from_city_idx ON ships (from_city);
CREATE INDEX ships_to_hub_idx ON ships (to_hub);
CREATE INDEX ships_status_idx ON ships (status) WHERE status IN ('queued', 'traveling', 'at_hub');

CREATE TABLE trade_orders (
  id UUID PRIMARY KEY,
  hub_id UUID NOT NULL REFERENCES hubs(id),
  kingdom_id UUID NOT NULL REFERENCES kingdoms(id),
  commodity TEXT NOT NULL,
  side SMALLINT NOT NULL, -- 0 = buy, 1 = sell
  price NUMERIC(10,4) NOT NULL,
  quantity INTEGER NOT NULL,
  placed_at_ms BIGINT NOT NULL,
  filled_quantity INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'open',
  canceled_at TIMESTAMPTZ
);

CREATE INDEX trade_orders_hub_commodity_side ON trade_orders (hub_id, commodity, side) WHERE status = 'open';
CREATE INDEX trade_orders_kingdom_idx ON trade_orders (kingdom_id);
CREATE INDEX trade_orders_placed_at_idx ON trade_orders (placed_at_ms);

CREATE TABLE price_observations (
  id UUID PRIMARY KEY,
  hub_id UUID NOT NULL REFERENCES hubs(id),
  commodity TEXT NOT NULL,
  price NUMERIC(10,4) NOT NULL,
  observed_at_ms BIGINT NOT NULL,
  source TEXT NOT NULL -- 'caravan', 'ship', 'hub'
);

CREATE INDEX price_observations_hub_commodity ON price_observations (hub_id, commodity, observed_at_ms DESC);